namespace TenderAI.Api.Services
{
    public sealed class BedrockOptions
    {
        public const string Section = "Bedrock";

        public string ModelId { get; set; } = "eu.amazon.nova-micro-v1:0";
        public string Region { get; set; } = "eu-central-1";
    }
}
