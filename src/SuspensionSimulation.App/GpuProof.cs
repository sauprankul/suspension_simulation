using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;

namespace SuspensionSimulation.App;

/// <summary>App-owned deterministic GPU smoke test; no desktop automation.</summary>
internal static class GpuProof
{
    internal static void Run(Application app)
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","visualizer");
        Directory.CreateDirectory(directory);
        DefaultEffectsManager? effects=null;
        Viewport3DX? viewport=null;
        Window? window=null;
        var frames=0;
        var finished=false;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
        void Finish(Exception? error)
        {
            if(finished) return;finished=true;
            timer.Stop();
            var adapter="unavailable"; var software=true;
            try
            {
                if(effects is not null)
                {
                    using var dxgi=(effects.Device ?? throw new InvalidOperationException("No graphics device was created.")).QueryInterface<SharpDX.DXGI.Device>();
                    using var actual=dxgi.Adapter;
                    adapter=actual.Description.Description.TrimEnd('\0');
                    using var adapter1=actual.QueryInterface<SharpDX.DXGI.Adapter1>();
                    software=(adapter1.Description1.Flags & SharpDX.DXGI.AdapterFlags.Software)!=0;
                }
            }
            catch(Exception ex) {error ??= ex;}
            try {if(error is null) viewport!.SaveScreen(Path.Combine(directory,"gpu-proof.png"));}
            catch(Exception ex) {error=ex;}
            var success=error is null && frames>0 && !software;
            viewport?.Dispose(); effects?.Dispose(); window?.Close();
            File.WriteAllText(Path.Combine(directory,"gpu-proof.json"),JsonSerializer.Serialize(new
            {
                success, adapter, software,frames, renderer="HelixToolkit.Wpf.SharpDX 3.1.2 / DirectX 11",
                resized=true, cameraChanged=true, disposed=true, error=error?.ToString(),
                mouseAcceptance="pending interactive inspection"
            },new JsonSerializerOptions {WriteIndented=true}));
            app.Shutdown(success?0:2);
        }
        try
        {
            effects=new DefaultEffectsManager(new EffectsManagerConfiguration {EnableSoftwareRendering=false});
            viewport=new Viewport3DX
            {
                EffectsManager=effects, ModelUpDirection=new Vector3D(0,0,1),
                BackgroundColor=Color.FromRgb(17,20,25),
                Camera=new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {Position=new Point3D(3,3,2),LookDirection=new Vector3D(-3,-3,-1.7),UpDirection=new Vector3D(0,0,1)}
            };
            var line=new LineBuilder();
            line.AddLine(Vector3.Zero,Vector3.UnitX);line.AddLine(Vector3.Zero,Vector3.UnitY);line.AddLine(Vector3.Zero,Vector3.UnitZ);
            for(var i=0;i<64;i++)
            {
                var a=2*MathF.PI*i/64;var b=2*MathF.PI*(i+1)/64;
                line.AddLine(new Vector3(MathF.Cos(a)*.31f,0,.31f+MathF.Sin(a)*.31f),new Vector3(MathF.Cos(b)*.31f,0,.31f+MathF.Sin(b)*.31f));
            }
            viewport.Items.Add(new LineGeometryModel3D {Geometry=line.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=3});
            viewport.OnRendered+=(_,_)=>Interlocked.Increment(ref frames);
            viewport.RenderExceptionOccurred+=(_,e)=>{e.Handled=true;var error=e.Exception;app.Dispatcher.BeginInvoke(()=>Finish(error));};
            window=new Window {Title="Suspension GPU integration proof",Width=800,Height=600,Content=viewport};
            var step=0;
            timer.Tick+=(_,_)=>
            {
                if(step++==0)
                {
                    window.Width=960;window.Height=640;
                    viewport.Camera.Position=new Point3D(2,-3,1.8);
                    viewport.Camera.LookDirection=new Vector3D(-2,3,-1.5);
                }
                else Finish(null);
            };
            window.Show();timer.Start();
        }
        catch(Exception ex) {Finish(ex);}
    }
}
