using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public interface ICompanyRecord { string CompanyKey { get; set; } }

public class CompanyProfile
{
    [Key, MaxLength(64)] public string Key { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(500)] public string Website { get; set; } = "";
    public bool IsDemo { get; set; }
}
