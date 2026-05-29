using Mistral.SDK.Converters;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mistral.SDK.DTOs
{
	public class ResponseFormat
	{
		[JsonConverter(typeof(JsonPropertyNameEnumConverter<ResponseFormatEnum>))]
		public enum ResponseFormatEnum
		{
			/// <summary>
			/// Enum json for value: json_object
			/// </summary>
			[JsonPropertyName("json_object")]
			JSON = 1,

			/// <summary>
			/// Enum json_schema for value: json_schema
			/// </summary>
			[JsonPropertyName("json_schema")]
			JSON_SCHEMA = 2
		}

		/// <summary>
		/// The output type
		/// </summary>
		[JsonPropertyName("type")]
		public ResponseFormatEnum Type { get; set; }

		/// <summary>
		/// The JSON Schema for the response (only applicable if type is json_schema)
		/// </summary>
		[JsonPropertyName("json_schema")]
		public ResponseFormatJsonSchema JsonSchema { get; set; }
	}

	public class ResponseFormatJsonSchema
	{
		/// <summary>
		/// The name of the schema
		/// </summary>
		[JsonPropertyName("name")]
		public string Name { get; set; }

		/// <summary>
		/// Whether to enforce strict validation against the schema
		/// </summary>
		[JsonPropertyName("strict")]
		public bool Strict { get; set; }

		/// <summary>
		/// The schema definition
		/// </summary>
		[JsonPropertyName("schema")]
		public JsonElement Schema { get; set; }
	}
}
