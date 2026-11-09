namespace DAL.Entities;

public class AllergenSubmission
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string SubmittedName { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Status { get; set; } = "PENDING";
    public string? PendingKey { get; set; }
    public string? RejectionReason { get; set; }
    public int? AllergenId { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
