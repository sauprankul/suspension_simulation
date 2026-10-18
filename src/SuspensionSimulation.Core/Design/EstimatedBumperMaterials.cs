using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SuspensionSimulation.Design;

/// <summary>Editable engineering starting values, never calibrated compression data.</summary>
public static class EstimatedBumperMaterials
{
    public const string Tpu85Source="https://ultrafusefff.jp/basf3d/wp-content/uploads/2024/11/Ultrafuse_TPU_85A_TDS_EN_v2.5.pdf";
    public const string Tpu95Source="https://move.forward-am.com/hubfs/AES%20Documentation/Flexible%20Filaments/TPU%2095A/TDS/Ultrafuse_TPU-95A_TDS_EN_v1.0.pdf";
    public const string FoamSource="https://www.basf.com/dam/jcr:0af1eb5a-0ae0-3a4b-84e0-7cf7e6f2511d/basf/www/kr/documents/ko/product/Cellasto_technical_brochure.pdf";
    public static string Evidence(string id)=>$"Engineering estimate v1; preset={id}\nSources: {Tpu85Source}\n{Tpu95Source}\n{FoamSource}\nTPU85/95: printed XY tensile Young moduli20/48.4MPa;90A linearly interpolated34.2MPa. Poisson0.49 and isotropic neo-Hookean compression extension assumed. Not a fitted compression law. Foam: BASF Fig1 40mm cylinder guide curves; assumed low lateral expansion nu0.05 and Ogden exponent2, approximate initial E0.5/0.65/0.9/1.2/1.55/1.9/2.3MPa at350..650kg/m3. These are engineering estimates of guide-curve scale, not manufacturer material coefficients or digitized fits. Material, print anisotropy, strain-rate/hysteresis and actual part correlation are unverified.\n";
    public static IEnumerable<BumperMaterialCard> Catalog()
    {
        foreach(var density in new[]{350,400,450,500,550,600,650})
        {
            var e=new[]{.5,.65,.9,1.2,1.55,1.9,2.3}[(density-350)/50]*1e6;const double nu=.05;
            var p=new Dictionary<string,double>{{"cp",e*nu/((1+nu)*(1-2*nu))},{"c1",e/(1+nu)},{"m1",2}};
            yield return Card("cellular-pu-"+density,$"Microcellular PU · {density} kg/m³ · Estimated",BumperMaterialFamily.MicrocellularPolyurethane,density,null,p,"febio.ogden-unconstrained.v1","W=cp/2*(J-1)^2+c1/m1^2*(sum(lambda_i^m1)-3-m1*ln(J))",.5,null);
        }
        foreach(var hardness in new[]{85,90,95})
        {
            var e=(hardness==85?20:hardness==90?34.2:48.4)*1e6;const double nu=.49;
            var p=new Dictionary<string,double>{{"c1",e/(4*(1+nu))},{"c2",0},{"k",e/(3*(1-2*nu))}};
            var print=new BumperPrintRecipe(PrintedGeometryRepresentation.ExplicitPrintedGeometry,"Generic TPU "+hardness+"A estimate",new(0,0,1),.0002,.0004,.00045,3,.00135,4,4,"Explicit CAD geometry; no stiffness scaling",100,"Unspecified; edit for actual print","Unspecified; edit for actual print");
            yield return Card("printed-tpu-"+hardness,$"Printed TPU {hardness}A · Estimated",BumperMaterialFamily.PrintedTpu,1140,hardness,p,"febio.mooney-rivlin-pressure2.v1","W=c1*(I1bar-3)+c2*(I2bar-3)+k/2*(J-1)^2",.3,print);
        }
    }
    private static BumperMaterialCard Card(string id,string name,BumperMaterialFamily family,double density,double? shore,Dictionary<string,double> p,string key,string energy,double range,BumperPrintRecipe? print)
    {
        var hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Evidence(id))));
        return new(id,name,family,name,density,shore,MaterialReadiness.Estimated,"Generic quasi-static loading; temperature/rate/print conditions require review",Evidence(id),[hash],new(key,p.ToImmutableDictionary(),energy,hash),print)
            {Validity=new(range,"quasi-static loading","Assumed exploration interval; not a measured compression validity limit")};
    }
    public static string[] Keys(BumperMaterialCard card)=>card.Law?.ModelKey switch
    {"febio.ogden-unconstrained.v1"=>["cp","c1","m1"],"febio.mooney-rivlin-pressure2.v1"=>["c1","c2","k"],_=>[]};
    public static BumperMaterialCard Override(BumperMaterialCard card,IReadOnlyList<double> values)
    {
        var keys=Keys(card);if(keys.Length!=3||values.Count!=3||values.Any(v=>!double.IsFinite(v))) throw new ArgumentException("This estimate requires three finite coefficients.");
        if(values[0]<=0||values[1]<0||(keys[2]=="k"?values[2]<=0:values[1]<=0||Math.Abs(values[2])<.01||Math.Abs(values[2])>20)) throw new ArgumentException("Use positive stiffness/bulk coefficients and a nonzero bounded foam exponent.");
        return card with {Readiness=MaterialReadiness.Estimated,Law=card.Law! with {ParametersSi=keys.Select((key,i)=>(key,values[i])).ToImmutableDictionary(p=>p.key,p=>p.Item2)}};
    }
}
