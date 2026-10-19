using System.Text;

namespace FactoryOps.Web.Services;

// The preview and download use the same CSV so examples cannot drift apart.
public static class CsvImportExamples
{
    public const string Customers =
        "CompanyName,ContactName,Phone,Email,BillingAddress,SiteName,Address,Suburb,State,Postcode,SiteContactName,SiteContactPhone\r\n" +
        "Example Customer Pty Ltd,Sample Contact,0400000000,orders@example.com,\"10 Example Road, Sydney NSW 2000\",Example Site,20 Sample Street,Sydney,NSW,2000,Sample Site Contact,0400000001\r\n";

    public const string Products =
        "Sku,Name,Description,Category,UnitOfMeasure,Dimensions,PackingSize,RoughIn,HeatingMethod,FlushingMethod,CeramicBody,Color,DisplayMode,NetWeight,GrossWeight,Specifications,Packaging,IsActive,Aliases\r\n" +
        "04412,Example product,Replace this sample row,Example category,each,690 x 395 x 500 mm,820 x 490 x 630 mm,200 / 300 / 400 mm,Instant heating,Jet siphon type,T3: No water tank; K3: With water tank,White / Matte Gray / Matte Black,Celsius / Fahrenheit,40 kg,45 kg,Replace with verified product data,Pack of 1,true,Example alias\r\n";

    public const string Stock =
        "Sku,UnitOfMeasure,CountedQuantity,ReorderPoint,LeadTimeDays,Reason\r\n" +
        "04412,each,100,20,7,Opening physical count\r\n" +
        "EXAMPLE-002,box,0,5,14,Opening physical count\r\n";

    // Include a UTF-8 BOM so spreadsheet applications recognise Unicode text.
    public static byte[] Download(string csv) => Encoding.UTF8.GetPreamble()
        .Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
}
