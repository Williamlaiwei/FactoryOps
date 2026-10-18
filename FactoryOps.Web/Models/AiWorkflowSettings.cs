using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

// Preferences for the upcoming intake workflow; this record never approves an order.
public class AiWorkflowSettings : ICompanyRecord
{
    [Key, MaxLength(64)] public string CompanyKey { get; set; } = "";
    public uint Version { get; set; }
    [MaxLength(100)] public string OrderModel { get; set; } = "";
    [MaxLength(100)] public string TranscriptionModel { get; set; } = "";
    [MaxLength(100)] public string EmbeddingModel { get; set; } = "";
    [MaxLength(32)] public string IntakeMode { get; set; } = "ReviewBeforeImport";
    public bool UsePhoneRecords { get; set; } = true;
    public bool UseEmailOrders { get; set; } = true;
    [MaxLength(254)] public string MailboxAddress { get; set; } = "";
    [MaxLength(16)] public string ReplyChannel { get; set; } = "Email";
    [MaxLength(16)] public string ReplyLanguage { get; set; } = "en-AU";
    [MaxLength(254)] public string ReplyFromAddress { get; set; } = "";
    [MaxLength(2000)] public string ReplySignature { get; set; } = "";
    [MaxLength(2000)] public string MissingInfoInstructions { get; set; } = "";
}

public class SettingsForm
{
    public uint Version { get; set; }
    [StringLength(100), Display(Name = "Order extraction and reply model")]
    public string? OrderModel { get; set; }
    [StringLength(100), Display(Name = "Phone transcription model")]
    public string? TranscriptionModel { get; set; }
    [StringLength(100), Display(Name = "Knowledge embedding model")]
    public string? EmbeddingModel { get; set; }
    [Required, RegularExpression("^(ReviewBeforeImport|CreateAiDraft)$"), Display(Name = "Order intake policy")]
    public string IntakeMode { get; set; } = "ReviewBeforeImport";
    [Display(Name = "Phone conversation records")]
    public bool UsePhoneRecords { get; set; } = true;
    [Display(Name = "Email orders and attachments")]
    public bool UseEmailOrders { get; set; } = true;
    [EmailAddress, StringLength(254), Display(Name = "Order inbox address")]
    public string? MailboxAddress { get; set; }
    [Required, RegularExpression("^(Email|Message|Both)$"), Display(Name = "Preferred reply channel")]
    public string ReplyChannel { get; set; } = "Email";
    [Required, RegularExpression("^(en-AU|zh-TW)$"), Display(Name = "Reply language")]
    public string ReplyLanguage { get; set; } = "en-AU";
    [EmailAddress, StringLength(254), Display(Name = "Reply sender address")]
    public string? ReplyFromAddress { get; set; }
    [StringLength(2000), Display(Name = "Reply signature")]
    public string? ReplySignature { get; set; }
    [StringLength(2000), Display(Name = "Missing-information reply instructions")]
    public string? MissingInfoInstructions { get; set; }

    public static SettingsForm From(AiWorkflowSettings settings) => new()
    {
        Version = settings.Version, OrderModel = settings.OrderModel,
        TranscriptionModel = settings.TranscriptionModel, EmbeddingModel = settings.EmbeddingModel,
        IntakeMode = settings.IntakeMode, UsePhoneRecords = settings.UsePhoneRecords,
        UseEmailOrders = settings.UseEmailOrders, MailboxAddress = settings.MailboxAddress,
        ReplyChannel = settings.ReplyChannel, ReplyLanguage = settings.ReplyLanguage,
        ReplyFromAddress = settings.ReplyFromAddress, ReplySignature = settings.ReplySignature,
        MissingInfoInstructions = settings.MissingInfoInstructions
    };

    public void ApplyTo(AiWorkflowSettings settings)
    {
        settings.OrderModel = OrderModel?.Trim() ?? "";
        settings.TranscriptionModel = TranscriptionModel?.Trim() ?? "";
        settings.EmbeddingModel = EmbeddingModel?.Trim() ?? "";
        settings.IntakeMode = IntakeMode;
        settings.UsePhoneRecords = UsePhoneRecords;
        settings.UseEmailOrders = UseEmailOrders;
        settings.MailboxAddress = MailboxAddress?.Trim() ?? "";
        settings.ReplyChannel = ReplyChannel;
        settings.ReplyLanguage = ReplyLanguage;
        settings.ReplyFromAddress = ReplyFromAddress?.Trim() ?? "";
        settings.ReplySignature = ReplySignature?.Trim() ?? "";
        settings.MissingInfoInstructions = MissingInfoInstructions?.Trim() ?? "";
    }
}
