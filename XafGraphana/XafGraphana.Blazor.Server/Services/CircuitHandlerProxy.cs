using DevExpress.ExpressApp.Blazor.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace XafGraphana.Blazor.Server.Services
{
    internal class CircuitHandlerProxy : CircuitHandler
    {
        readonly IScopedCircuitHandler scopedCircuitHandler;
        public CircuitHandlerProxy(IScopedCircuitHandler scopedCircuitHandler)
        {
            this.scopedCircuitHandler = scopedCircuitHandler;
        }
        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            XafMetrics.ActiveSessions.Inc();
            return scopedCircuitHandler.OnCircuitOpenedAsync(cancellationToken);
        }
        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            return scopedCircuitHandler.OnConnectionUpAsync(cancellationToken);
        }
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            XafMetrics.ActiveSessions.Dec();
            return scopedCircuitHandler.OnCircuitClosedAsync(cancellationToken);
        }
        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            return scopedCircuitHandler.OnConnectionDownAsync(cancellationToken);
        }
    }
}
