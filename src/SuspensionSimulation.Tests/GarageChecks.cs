using SuspensionSimulation.Design;

namespace SuspensionSimulation.Tests;

internal static class GarageChecks
{
    internal static void Persistence()
    {
        var directory=Path.Combine(Path.GetTempPath(),"suspension-garage-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new GarageLibrary(directory);var first=store.Create("Stock GR86");var second=store.Create("Stock BRZ");
            var custom=store.Duplicate(first) with {Name="My car",Notes="Independent settings"};custom=store.Save(custom);
            if(new GarageLibrary(directory).List().Count!=3 || store.Load(first.Id)!.Name!=first.Name || store.Load(second.Id)!.Revision!=1) throw new Exception("Garage entries are not independent or persistent.");
            var archived=store.Save(custom with {Archived=true});var restored=store.Save(archived with {Archived=false});
            if(store.Load(restored.Id)!.Archived || restored.Revision!=4 || restored.Configuration is not null) throw new Exception("Archive recovery or unresolved status failed.");
            try {store.Save(custom);throw new Exception("Stale edit accepted.");} catch(InvalidOperationException) { }
            try {store.Load("../outside");throw new Exception("Unsafe identity accepted.");} catch(ArgumentException) { }
            if(Directory.GetFiles(directory,"*.json",SearchOption.AllDirectories).Length!=6) throw new Exception("Garage revision history was overwritten.");
        }
        finally {Directory.Delete(directory,recursive:true);}
    }
}
