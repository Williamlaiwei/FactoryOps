using Microsoft.AspNetCore.Mvc;
using FactoryOps.Web.Models;
namespace FactoryOps.Web.Controllers;
public class WorkOrdersController: Controller
{
    private static readonly object Sync =new();
    private static readonly List<WorkOrder> Orders =new()
    {
        new WorkOrder
        {
            Id =1001,
            Title ="Staircase Formwork",
            ProductType ="Fast Tread",
            DueDate =DateTime.Today.AddDays(5),
            Status ="In Progress"
        },
        new WorkOrder
        {
            Id=1002,
            Title ="Floor Decking",
            ProductType="BlueDeck",
            Quantity=50,
            DueDate =DateTime.Today.AddDays(2),
            Status ="Pending"
        },
        new WorkOrder
        {
            Id =1003,
            Title ="Door Frames",
            ProductType ="SureFit",
            Quantity=30,
            DueDate =DateTime.Today.AddDays(7),
            Status =" Completed"
        }
    };


    [HttpGet]

    public IActionResult Index()
    {
        lock(Sync)
        {
            return View(Orders.OrderBy(x=> x.Id).ToList());
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(
        string productType,
        int quantity,
        DateTime dueDate)
    {
        string[] products=
        {
            "Fast Thread", "BlueDeck","SureFit"
        };

        if(!products.Contains(productType) ||quantity<=0 || dueDate.Date<DateTime.Today)
        {
            return BadRequest("Invalid order details.");
        }
        
        lock (Sync)
        {
            int nextId =Orders.Max(x=> x.Id)+1;
            Orders.Add(new WorkOrder
            {
                Id= nextId,
                Title =$"Production -{productType}",
                ProductType =productType,
                Quantity=quantity,
                DueDate=dueDate,
                Status="Pending"






            }
            
            
            
            
            
            );
        }
        
        return RedirectToAction(nameof(Index));

    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateStatus(int id, string status)
    {
        string[] allowed=
        {
            "Pending", "In Progress","Completed"
        };


        if (!allowed.Contains(status))
        {
            return BadRequest("Invalid status.");
        }
        lock(Sync)
        {
            
            var order = Orders.FirstOrDefault(x=>x.Id == id);

            if (order ==null)
                return NotFound();

            order.Status =status;
        }
        return RedirectToAction(nameof(Index));
    }



}