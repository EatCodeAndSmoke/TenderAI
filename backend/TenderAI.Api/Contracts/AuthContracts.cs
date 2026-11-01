using System.ComponentModel.DataAnnotations;

namespace TenderAI.Api.Contracts
{
    public class AuthRequest
    {
        [Required]
        public string CustomerId { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }

    public record AuthResponse(string CustomerId, string Name);
}
