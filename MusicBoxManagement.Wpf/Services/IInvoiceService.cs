using System;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Shared by the two data providers and the existing invoice/print view models.
    public interface IInvoiceService
    {
        InvoiceSearch Search(LoginSession actor, string invoiceNumber = null, DateTime? fromDate = null, DateTime? toDate = null, string phoneNumber = null);
        InvoiceDetails Read(LoginSession actor, int invoiceId);
        InvoiceDetails ReadForPrint(LoginSession actor, int invoiceId);
    }
}
