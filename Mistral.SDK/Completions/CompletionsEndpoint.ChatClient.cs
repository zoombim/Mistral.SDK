using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.AI;
using Mistral.SDK.DTOs;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mistral.SDK.Completions
{
    public partial class CompletionsEndpoint : IChatClient
    {
        private static readonly Regex s_validFunctionCallIdRegex = new("^[a-zA-Z0-9]{9}$");

        async Task<ChatResponse> IChatClient.GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken)
        {
            var response = await GetCompletionAsync(CreateRequest(messages, options), cancellationToken).ConfigureAwait(false);

            Microsoft.Extensions.AI.ChatMessage message = new(ChatRole.Assistant, ProcessResponseContent(response))
            {
                MessageId = response.Id ?? Guid.NewGuid().ToString("N"),
            };

            var completion = new ChatResponse(message)
            {
                ModelId = response.Model,
                ResponseId = response.Id,
                RawRepresentation = response,
            };

            if (response.Usage is { } usage)
            {
                completion.Usage = new UsageDetails()
                {
                    InputTokenCount = usage.PromptTokens,
                    OutputTokenCount = usage.CompletionTokens,
                    TotalTokenCount = usage.TotalTokens
                };
            }

            return completion;
        }

        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var response in StreamCompletionAsync(CreateRequest(messages, options), cancellationToken).ConfigureAwait(false))
            {
                foreach (var choice in response.Choices)
                {
                    ChatRole role = choice.Delta?.Role switch
                    {
                        DTOs.ChatMessage.RoleEnum.System => ChatRole.System,
                        DTOs.ChatMessage.RoleEnum.Assistant => ChatRole.Assistant,
                        _ => ChatRole.User,
                    };

                    ChatFinishReason? finishReason = choice.FinishReason switch
                    {
                        Choice.FinishReasonEnum.Length => ChatFinishReason.Length,
                        Choice.FinishReasonEnum.ModelLength => ChatFinishReason.Length,
                        _ => ChatFinishReason.Stop
                    };

                    var update = new ChatResponseUpdate(role, choice.Delta?.Content)
                    {
                        MessageId = response.Id,
                        ModelId = response.Model,
                        RawRepresentation = response,
                        ResponseId = response.Id,
                        FinishReason = finishReason,
                    };

                    if (choice.Delta?.ToolCalls is { Count: > 0 })
                    {
                        foreach (var toolCall in choice.Delta.ToolCalls)
                        {
                            Dictionary<string, object> arguments = null;
                            if (toolCall.Function.Arguments is not null)
                            {
                                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Function.Arguments.ToString());
                            }

                            update.Contents.Add(new FunctionCallContent(
                                toolCall.Id,
                                toolCall.Function.Name,
                                arguments));
                        }
                    }

                    yield return update;
                }

                if (response.Usage is { } usage)
                {
                    yield return new ChatResponseUpdate()
                    {
                        Contents = new List<AIContent>()
                        {
                            new UsageContent(new UsageDetails()
                            {
                                InputTokenCount = usage.PromptTokens,
                                OutputTokenCount = usage.CompletionTokens,
                                TotalTokenCount = usage.TotalTokens
                            })
                        },
                        MessageId = response.Id,
                        ModelId = response.Model,
                        ResponseId = response.Id,
                    };
                }
            }
        }

        private ChatCompletionRequest CreateRequest(IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages, ChatOptions options)
        {
            ChatCompletionRequest request = options?.RawRepresentationFactory?.Invoke(this) as ChatCompletionRequest ?? new();

            request.Messages ??= [];

            if (options?.Instructions is { } instructions)
            {
                request.Messages.Add(new DTOs.ChatMessage(DTOs.ChatMessage.RoleEnum.System, instructions));
            }

            request.Messages.AddRange(chatMessages.SelectMany(m =>
            {
                return ToChatMessageDTO(m);

                static IEnumerable<DTOs.ChatMessage> ToChatMessageDTO(Microsoft.Extensions.AI.ChatMessage m)
                {
                    DTOs.ChatMessage.RoleEnum role =
                        m.Role == ChatRole.System ? DTOs.ChatMessage.RoleEnum.System :
                        m.Role == ChatRole.Assistant ? DTOs.ChatMessage.RoleEnum.Assistant :
                        m.Role == ChatRole.Tool ? DTOs.ChatMessage.RoleEnum.Tool :
                        DTOs.ChatMessage.RoleEnum.User;

                    // We collect all multimodal chunks for this message
                    List<ChatMessageContentChunk> chunks = null;
                    List<ToolCall> toolCalls = null;

                    foreach (AIContent content in m.Contents)
                    {
                        switch (content)
                        {
                            case TextContent tc:
                                (chunks ??= []).Add(new ChatMessageContentChunk("text")
                                {
                                    Text = tc.Text
                                });
                                break;

                            case DataContent dc:
                                AddUrlChunk(ref chunks, dc.MediaType, dc.Uri);
                                break;

                            case UriContent uc:
                                AddUrlChunk(ref chunks, uc.MediaType, uc.Uri.AbsoluteUri);
                                break;

                            case FunctionCallContent fcc:
                                (toolCalls ??= []).Add(new()
                                {
                                    Id = fcc.CallId,
                                    Function = new ToolCallParameter()
                                    {
                                        Arguments = JsonSerializer.SerializeToNode(fcc.Arguments),
                                        Name = fcc.Name,
                                    }
                                });
                                break;

                            case FunctionResultContent frc:
                                yield return new DTOs.ChatMessage(frc.CallId, frc.CallId, frc.Result?.ToString());
                                break;
                        }
                    }

                    if (toolCalls is { Count: > 0 })
                    {
                        yield return new DTOs.ChatMessage
                        {
                            Role = DTOs.ChatMessage.RoleEnum.Assistant,
                            Content = chunks is { Count: > 0 } && chunks[0].Type == "text" ? chunks[0].Text : string.Empty,
                            ToolCalls = toolCalls,
                        };
                        yield break;
                    }

                    // Send the main message (text or multimodal)
                    if (chunks == null || chunks.Count == 0)
                        yield break;

                    // optimization: if it is only a text chunk, we keep the string format (backward compatible)
                    if (chunks.Count == 1 && chunks[0].Type == "text")
                    {
                        yield return new DTOs.ChatMessage(role, chunks[0].Text ?? string.Empty);
                    }
                    else
                    {
                        yield return new DTOs.ChatMessage()
                        {
                            Role = role,
                            Content = string.Empty, // kept for compatibility
                            ContentChunks = chunks  // will be serialized as an array
                        };
                    }
                }
            }));

            // Mistral has a bunch of requirements about the structure of messages. Try to avoid the most common issues.
            {
                const string EmptyMessage = "\u200b";

                // There needs to be at least one user or assistant message.
                if (request.Messages.Count == 0 ||
                    request.Messages.All(m => m.Role is not (DTOs.ChatMessage.RoleEnum.User or DTOs.ChatMessage.RoleEnum.Assistant)))
                {
                    request.Messages.Add(new DTOs.ChatMessage(DTOs.ChatMessage.RoleEnum.User, EmptyMessage));
                }

                // System messages should be at the beginning and consolidated into one message.
                string systemMessage = string.Join("\n", request.Messages.Where(m => m.Role == DTOs.ChatMessage.RoleEnum.System).Select(m => m.Content));
                if (!string.IsNullOrWhiteSpace(systemMessage))
                {
                    request.Messages.RemoveAll(m => m.Role == DTOs.ChatMessage.RoleEnum.System);
                    request.Messages.Insert(0, new(DTOs.ChatMessage.RoleEnum.System, systemMessage));
                }

                // Function call IDs must be in a very specific format, nine [a-zA-Z0-9] characters.
                // If any aren't, remove them.
                for (int i = 0; i < request.Messages.Count; i++)
                {
                    var m = request.Messages[i];
                    if (m.ToolCallId is not null && !s_validFunctionCallIdRegex.IsMatch(m.ToolCallId))
                    {
                        request.Messages[i] = null;
                    }
                    else if (m.ToolCalls is { Count: > 0 })
                    {
                        m.ToolCalls.RemoveAll(static tc => !s_validFunctionCallIdRegex.IsMatch(tc.Id));
                        if (m.ToolCalls.Count == 0)
                        {
                            request.Messages[i] = null;
                        }
                    }
                }
                request.Messages.RemoveAll(static m => m is null);

                // User messages should be consolidated so that two user messages don't appear in a row.
                for (int i = 0; i < request.Messages.Count - 1; i++)
                {
                    if (request.Messages[i].Role != DTOs.ChatMessage.RoleEnum.User)
                    {
                        continue;
                    }

                    int next = i + 1;
                    while (next < request.Messages.Count && request.Messages[next].Role == DTOs.ChatMessage.RoleEnum.User) next++;

                    if (i + 1 < next)
                    {
                        // When merging consecutive user messages, if one of them has ContentChunks, we merge the chunks.
                        for (int j = i + 1; j < next; j++)
                            MergeUserMessages(request.Messages[i], request.Messages[j]);
                        request.Messages.RemoveRange(i + 1, next - (i + 1));
                    }
                }

                // Tool messages must be followed by an assistant message if there's anything next.
                for (int i = 0; i < request.Messages.Count; i++)
                {
                    if (request.Messages[i].Role == DTOs.ChatMessage.RoleEnum.Tool &&
                        i + 1 < request.Messages.Count &&
                        request.Messages[i + 1].Role != DTOs.ChatMessage.RoleEnum.Assistant)
                    {
                        request.Messages.Insert(i + 1, new DTOs.ChatMessage(DTOs.ChatMessage.RoleEnum.Assistant, EmptyMessage));
                    }
                }

                // The last message must not be Assistant.
                if (request.Messages[request.Messages.Count - 1].Role == DTOs.ChatMessage.RoleEnum.Assistant)
                {
                    request.Messages.Add(new DTOs.ChatMessage(DTOs.ChatMessage.RoleEnum.User, EmptyMessage));
                }
            }

            request.Model ??= options?.ModelId;
            request.Temperature ??= (decimal?)options?.Temperature;
            request.TopP ??= (decimal?)options?.TopP;
            request.MaxTokens ??= options?.MaxOutputTokens;
            request.ParallelToolCalls = options?.AllowMultipleToolCalls ?? request.ParallelToolCalls;
            request.RandomSeed ??= (int?)options?.Seed;

            if (options?.ResponseFormat is ChatResponseFormatJson)
            {
                request.ResponseFormat ??= new ResponseFormat() { Type = ResponseFormat.ResponseFormatEnum.JSON };
            }

            List<Common.Tool> tools = null;
            if (options?.Tools is not null)
            {
                tools = options
                    .Tools
                    .OfType<AIFunctionDeclaration>()
                    .Select(f => new Common.Tool(new Common.Function(
                        f.Name,
                        f.Description,
                        JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<FunctionParameters>(f.JsonSchema)))))
                    .ToList();
            }

            if (tools is { Count: > 0 })
            {
                if (request.Tools is null)
                {
                    request.Tools = tools;
                }
                else
                {
                    tools.AddRange(request.Tools);
                    request.Tools = tools;
                }

                if (options.ToolMode is RequiredChatToolMode r)
                {
                    request.ToolChoice = ToolChoiceType.Any;
                }
                else if (options.ToolMode is AutoChatToolMode or null)
                {
                    request.ToolChoice = ToolChoiceType.Auto;
                }
                else if (options.ToolMode is NoneChatToolMode)
                {
                    request.ToolChoice = ToolChoiceType.none;
                }
            }

            return request;

            static void MergeUserMessages(DTOs.ChatMessage into, DTOs.ChatMessage other)
            {
                bool intoHasChunks = into.ContentChunks is { Count: > 0 };
                bool otherHasChunks = other.ContentChunks is { Count: > 0 };

                // Fast path: plain text on both sides, keep the legacy string form.
                if (!intoHasChunks && !otherHasChunks)
                {
                    into.Content = string.Join("\n", into.Content, other.Content);
                    return;
                }

                // Otherwise normalise everything to chunks so no text is lost.
                var merged = new List<ChatMessageContentChunk>();
                AppendAsChunks(merged, into);
                AppendAsChunks(merged, other);

                into.ContentChunks = merged;
                into.Content = string.Empty;

                static void AppendAsChunks(List<ChatMessageContentChunk> target, DTOs.ChatMessage msg)
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                        target.Add(new ChatMessageContentChunk("text") { Text = msg.Content });

                    if (msg.ContentChunks is { Count: > 0 })
                        target.AddRange(msg.ContentChunks);
                }
            }

            static void AddUrlChunk(ref List<ChatMessageContentChunk> chunks, string mediaType, string url)
            {
                bool isImage = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                (chunks ??= []).Add(new ChatMessageContentChunk(isImage ? "image_url" : "document_url")
                {
                    ImageUrl = isImage ? url : null,
                    DocumentUrl = !isImage ? url : null
                });
            }
        }

        private static List<AIContent> ProcessResponseContent(ChatCompletionResponse response)
        {
            List<AIContent> contents = new();

            foreach (var content in response.Choices)
            {
                if (content.Message.ToolCalls is not null)
                {
                    contents.Add(new TextContent(content.Message.Content));

                    foreach (var toolCall in content.Message.ToolCalls)
                    {
                        Dictionary<string, object> arguments = null;
                        if (toolCall.Function.Arguments is not null)
                        {
                            arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Function.Arguments.ToString());
                        }

                        contents.Add(new FunctionCallContent(
                            toolCall.Id,
                            toolCall.Function.Name,
                            arguments));
                    }
                }
                else
                {
                    contents.Add(new TextContent(content.Message.Content));
                }
            }

            return contents;
        }

        void IDisposable.Dispose() { }

        object IChatClient.GetService(Type serviceType, object serviceKey) =>
            serviceKey is not null ? null :
            serviceType == typeof(ChatClientMetadata) ? _metadata ??= new ChatClientMetadata(nameof(MistralClient), new Uri(Url)) :
            serviceType.IsInstanceOfType(this) ? this : null;

        private ChatClientMetadata _metadata;

        private sealed class FunctionParameters
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "object";

            [JsonPropertyName("required")]
            public List<string> Required { get; set; } = [];

            [JsonPropertyName("properties")]
            public Dictionary<string, JsonElement> Properties { get; set; } = [];
        }
    }

    
}
