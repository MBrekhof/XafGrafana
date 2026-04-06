using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.EntityFrameworkCore;

namespace XafGrafana.Module.BusinessObjects;

public enum OrderStatus
{
    New,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}

[DefaultClassOptions]
[NavigationItem("Business")]
public class Order : BaseObject
{
    public virtual DateTime OrderDate { get; set; }

    [Precision(18, 2)]
    public virtual decimal Amount { get; set; }

    public virtual OrderStatus Status { get; set; }

    public virtual Customer Customer { get; set; }
}
