using FactoryOps.Web.Models;

namespace FactoryOps.Web.Services;

public class CompanyContext
{
    public CompanyProfile Profile { get; set; } = new() { Key = "fti-demo", Name = "FTI Group Demo", IsDemo = true };
    public string Key => Profile.Key;
}
