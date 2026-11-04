namespace TenderAI.Api.Data.Entities
{
    public sealed class PromptSettingEntity
    {
        public const int MaximumLength = 20000;

        public required string Key { get; set; }
        public string? Instructions { get; set; }
        public int Revision { get; set; }
    }
}
