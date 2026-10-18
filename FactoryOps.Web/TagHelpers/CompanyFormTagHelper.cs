using System.Text.Encodings.Web;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FactoryOps.Web.TagHelpers;

[HtmlTargetElement("form")]
public class CompanyFormTagHelper(CompanyContext company) : TagHelper
{
    public override int Order => 1000;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (output.Attributes["method"]?.Value?.ToString()?.Equals("post", StringComparison.OrdinalIgnoreCase) == true)
            output.PostContent.AppendHtml($"<input type=\"hidden\" name=\"__CompanyKey\" value=\"{HtmlEncoder.Default.Encode(company.Key)}\" />");
    }
}
