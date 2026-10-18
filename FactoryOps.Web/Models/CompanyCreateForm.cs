using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class CompanyCreateForm : IValidatableObject
{
    [Required, StringLength(200), Display(Name = "Company name")]
    public string Name { get; set; } = "";

    [StringLength(500), Display(Name = "Website (optional)")]
    public string? Website { get; set; }

    [Required]
    public Guid? CreationToken { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreationToken == Guid.Empty)
            yield return new("Reload the Create Company page before submitting.", new[] { nameof(CreationToken) });
        if (!string.IsNullOrWhiteSpace(Website) &&
            (!Uri.TryCreate(Website.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            yield return new("Enter a valid HTTPS website, or leave it blank.", new[] { nameof(Website) });
    }
}
