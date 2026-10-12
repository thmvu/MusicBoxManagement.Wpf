using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MusicBoxManagement.Wpf.Printing
{
    // The native dialog/send boundary can be replaced in tests without accessing a printer.
    public interface IInvoicePrinter
    {
        bool Select();
        Size PrintableArea { get; }
        void Send(DocumentPaginator document, string jobName);
    }
    public sealed class InvoicePrinter : IInvoicePrinter
    {
        private PrintDialog dialog;
        public bool Select()
        {
            dialog = new PrintDialog { UserPageRangeEnabled = false,
                PrintTicket = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
                    PageOrientation = PageOrientation.Portrait } };
            return dialog.ShowDialog() == true;
        }
        public Size PrintableArea => new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        public void Send(DocumentPaginator document, string jobName) => dialog.PrintDocument(document, jobName);
    }
}
