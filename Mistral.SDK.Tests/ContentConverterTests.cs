using System.Text.Json;
using Mistral.SDK.DTOs;

namespace Mistral.SDK.Tests
{
    [TestClass]
    public class ContentConverterTests
    {
        [TestMethod]
        public void TestContentConverterWithStringContent()
        {
            // Regular model response with simple string content
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
                                    ""text"": ""Let me think about this...""
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

            // With #39, array content lands in ContentChunks, not Content string
            Assert.IsNotNull(message.ContentChunks);
            Assert.AreEqual(2, message.ContentChunks.Count);
            Assert.AreEqual("thinking", message.ContentChunks[0].Type);
            Assert.AreEqual("text", message.ContentChunks[1].Type);
            Assert.AreEqual("Here is my response.", message.ContentChunks[1].Text);

            // Content itself should be empty, not a raw JSON string
            Assert.AreEqual(string.Empty, message.Content);
        }

        [TestMethod]
        public void TestContentConverterWithNullContent()
        {
            // Response with null content
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
            // Create a message with string content
            var message = new ChatMessage
            {
                Role = ChatMessage.RoleEnum.Assistant,
                Content = "Test content"
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(message, MistralClient.JsonSerializationOptions);

            // Deserialize back
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

            // Delta content chunks should be parsed
            Assert.IsNotNull(delta.ContentChunks);
            Assert.AreEqual(1, delta.ContentChunks.Count);
            Assert.AreEqual("text", delta.ContentChunks[0].Type);
            Assert.AreEqual("Streaming text...", delta.ContentChunks[0].Text);
        }
    }
}