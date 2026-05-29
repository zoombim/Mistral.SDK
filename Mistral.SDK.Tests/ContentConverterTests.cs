using System.Text.Json;
using Mistral.SDK.DTOs;
using Mistral.SDK.Completions;
using Microsoft.Extensions.AI;
using ChatMessage = Mistral.SDK.DTOs.ChatMessage;

namespace Mistral.SDK.Tests
{
    [TestClass]
    public class ContentConverterTests
    {
        [TestMethod]
        public void TestContentConverterWithStringContent()
        {
            string json = @"{
                ""id"": ""test123"",
                ""object"": ""chat.completion"",
                ""created"": 1234567890,
                ""model"": ""mistral-small-2506"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""This is a simple string response.""
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 10,
                    ""completion_tokens"": 20,
                    ""total_tokens"": 30
                }
            }";

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json, MistralClient.JsonSerializationOptions);

            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Choices);
            Assert.AreEqual(1, response.Choices.Count);
            Assert.IsNotNull(response.Choices[0].Message);
            Assert.AreEqual("This is a simple string response.", response.Choices[0].Message.Content);
        }

        [TestMethod]
        public void TestContentConverterWithArrayContent()
        {
            string json = @"{
                ""id"": ""25b5475d6e2c48c9a9d80a48a4f302a3"",
                ""object"": ""chat.completion"",
                ""created"": 1761293297,
                ""model"": ""magistral-medium-latest"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": [
                                {
                                    ""type"": ""thinking"",
                                    ""thinking"": [
                                        {
                                            ""type"": ""text"",
                                            ""text"": ""Let me think about this...""
                                        }
                                    ],
                                    ""closed"": true
                                },
                                {
                                    ""type"": ""text"",
                                    ""text"": ""Here is my response.""
                                }
                            ]
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 10,
                    ""completion_tokens"": 1360,
                    ""total_tokens"": 1370
                }
            }";

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json, MistralClient.JsonSerializationOptions);

            Assert.IsNotNull(response);
            var message = response.Choices[0].Message;
            Assert.IsNotNull(message);

            Assert.IsNotNull(message.ContentChunks);
            Assert.AreEqual(2, message.ContentChunks.Count);

            // Thinking chunk
            Assert.AreEqual("thinking", message.ContentChunks[0].Type);
            Assert.IsNotNull(message.ContentChunks[0].Thinking);
            Assert.AreEqual(1, message.ContentChunks[0].Thinking.Count);
            Assert.AreEqual("text", message.ContentChunks[0].Thinking[0].Type);
            Assert.AreEqual("Let me think about this...", message.ContentChunks[0].Thinking[0].Text);

            // Text chunk
            Assert.AreEqual("text", message.ContentChunks[1].Type);
            Assert.AreEqual("Here is my response.", message.ContentChunks[1].Text);

            Assert.AreEqual(string.Empty, message.Content);
        }

        [TestMethod]
        public void TestContentConverterWithNullContent()
        {
            string json = @"{
                ""id"": ""test123"",
                ""object"": ""chat.completion"",
                ""created"": 1234567890,
                ""model"": ""mistral-small-2506"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": null
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 10,
                    ""completion_tokens"": 0,
                    ""total_tokens"": 10
                }
            }";

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json, MistralClient.JsonSerializationOptions);

            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Choices);
            Assert.AreEqual(1, response.Choices.Count);
            Assert.IsNotNull(response.Choices[0].Message);
            Assert.IsNull(response.Choices[0].Message.Content);
        }

        [TestMethod]
        public void TestContentConverterRoundTrip()
        {
            var message = new ChatMessage
            {
                Role = ChatMessage.RoleEnum.Assistant,
                Content = "Test content"
            };

            var json = JsonSerializer.Serialize(message, MistralClient.JsonSerializationOptions);
            var deserialized = JsonSerializer.Deserialize<ChatMessage>(json, MistralClient.JsonSerializationOptions);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("Test content", deserialized.Content);
            Assert.AreEqual(ChatMessage.RoleEnum.Assistant, deserialized.Role);
        }

        [TestMethod]
        public void TestContentConverterStreamingWithArrayContent()
        {
            string json = @"{
                ""id"": ""test123"",
                ""object"": ""chat.completion.chunk"",
                ""created"": 1234567890,
                ""model"": ""magistral-medium-latest"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""delta"": {
                            ""role"": ""assistant"",
                            ""content"": [
                                {
                                    ""type"": ""text"",
                                    ""text"": ""Streaming text...""
                                }
                            ]
                        },
                        ""finish_reason"": null
                    }
                ]
            }";

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json, MistralClient.JsonSerializationOptions);

            Assert.IsNotNull(response);
            var delta = response.Choices[0].Delta;
            Assert.IsNotNull(delta);

            Assert.IsNotNull(delta.ContentChunks);
            Assert.AreEqual(1, delta.ContentChunks.Count);
            Assert.AreEqual("text", delta.ContentChunks[0].Type);
            Assert.AreEqual("Streaming text...", delta.ContentChunks[0].Text);
        }

        [TestMethod]
        public void TestProcessResponseContentReturnsThinkingAsTextReasoningContent()
        {
            string json = @"{
                ""id"": ""25b5475d6e2c48c9a9d80a48a4f302a3"",
                ""object"": ""chat.completion"",
                ""created"": 1761293297,
                ""model"": ""magistral-medium-latest"",
                ""choices"": [
                    {
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": [
                                {
                                    ""type"": ""thinking"",
                                    ""thinking"": [
                                        {
                                            ""type"": ""text"",
                                            ""text"": ""Let me think about this...""
                                        }
                                    ],
                                    ""closed"": true
                                },
                                {
                                    ""type"": ""text"",
                                    ""text"": ""Here is my response.""
                                }
                            ]
                        },
                        ""finish_reason"": ""stop""
                    }
                ],
                ""usage"": {
                    ""prompt_tokens"": 10,
                    ""completion_tokens"": 1360,
                    ""total_tokens"": 1370
                }
            }";

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json, MistralClient.JsonSerializationOptions);
            var contents = CompletionsEndpoint.ProcessResponseContent(response);

            Assert.AreEqual(2, contents.Count);

            var reasoningContent = contents[0] as TextReasoningContent;
            Assert.IsNotNull(reasoningContent);
            Assert.AreEqual("Let me think about this...", reasoningContent.Text);

            var textContent = contents[1] as TextContent;
            Assert.IsNotNull(textContent);
            Assert.AreEqual("Here is my response.", textContent.Text);
        }
    }
}