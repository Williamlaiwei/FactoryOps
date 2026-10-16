using Microsoft.AspNetCore.Mvc;
using FactoryOps.Web.Models;
namespace FactoryOps.Web.Controllers;
public class WorkOrdersController: Controller
{
    public IActionResult Index()
    {
        var orders =new List<WorkOrder>
        {
            new WorkOrder
            {
                Id = 1,
                Title =" Machine Maintance",
                Status ="Pending"
            },
            new WorkOrder
            {
                Id=2,
                Title = "Production Check",
                Status ="Completed"
            }
            
        };
        return View(orders);
    }
}