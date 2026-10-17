namespace SuspensionSimulation.Components;

/// <summary>Unilateral rigid travel boundaries. Reaction is solved by equilibrium, never a penalty spring.</summary>
public sealed record WheelTravelConstraint
{
    public string Id {get;}
    public double MinimumJounceM {get;}
    public double MaximumJounceM {get;}
    public WheelTravelConstraint(string id,double minimumJounceM,double maximumJounceM)
    {
        if(string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A travel constraint needs an id.");
        if(!double.IsFinite(minimumJounceM)||!double.IsFinite(maximumJounceM)||minimumJounceM>=maximumJounceM||minimumJounceM>0||maximumJounceM<0)
            throw new ArgumentException("Travel constraints must have finite minimum < maximum and include static zero.");
        Id=id;MinimumJounceM=minimumJounceM;MaximumJounceM=maximumJounceM;
    }
}
