using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FactoryOps.Web.Services;

public class CompanyFormFilter(CompanyContext company) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsPost(request.Method) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            if (form.TryGetValue("__CompanyKey", out var key) && key != company.Key)
            {
                if (context.Controller is Controller controller)
                    controller.TempData["CompanyMessage"] = "The active company changed in another tab. Reload the form before saving; nothing was changed.";
                context.Result = new RedirectToActionResult("Index", "Companies", null);
                return;
            }
        }
        await next();
    }
}
