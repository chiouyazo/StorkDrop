using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StorkDrop.Contracts;

PluginContext context = new PluginContext
{
    ProductId = "acme-dashboard",
    Version = "2.1.0",
    InstallPath = Path.Combine(Path.GetTempPath(), "StorkDropExample"),
};

Dictionary<string, string> config = new Dictionary<string, string>
{
    ["database"] = "Production",
    ["apiUrl"] = "https://api.acme.local",
    ["port"] = "8443",
    ["enableSsl"] = "true",
    ["features"] = "Reporting,API",
};

await StorkPluginDebugger.RunAsync<ExampleProduct.Installer>(context, config);
