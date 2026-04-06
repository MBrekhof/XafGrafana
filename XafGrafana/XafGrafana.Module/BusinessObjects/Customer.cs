using System.Collections.ObjectModel;
using System.ComponentModel;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafGrafana.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
[NavigationItem("Business")]
public class Customer : BaseObject
{
    public virtual string Name { get; set; }

    public virtual string Email { get; set; }

    public virtual string City { get; set; }

    public virtual IList<Order> Orders { get; set; } = new ObservableCollection<Order>();
}
