using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafGraphana.Module.BusinessObjects;

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

    public virtual decimal Amount { get; set; }

    public virtual OrderStatus Status { get; set; }

    public virtual Customer Customer { get; set; }
}
