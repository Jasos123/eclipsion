namespace Content.Server._Crescent.Economy;

/// <summary>
/// Which listed company tracks which faction, keyed by the faction tag as written on mobs and stations.
///
/// One map rather than one per system: two systems now depend on this association meaning exactly the
/// same thing. The signal system lists a company when its faction turns up, and the war liquidation
/// system decides whose shares get seized by it. If those two ever disagreed about which company is
/// "SHI", a player could be liquidated out of stock they were never holding.
/// </summary>
public static class FactionStockCompanies
{
    public static readonly IReadOnlyDictionary<string, string> ByFaction =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHI"] = "stock-company-shi",
            ["TFSC"] = "stock-company-tfsc",
            ["DSM"] = "stock-company-dsm",
            ["NCWL"] = "stock-company-ncwl",
        };

    public static bool TryGetCompany(string faction, out string companyId)
    {
        if (!string.IsNullOrEmpty(faction))
            return ByFaction.TryGetValue(faction, out companyId!);

        companyId = string.Empty;
        return false;
    }

    public static bool TryGetFaction(string companyId, out string faction)
    {
        if (!string.IsNullOrEmpty(companyId))
        {
            foreach (var (candidateFaction, candidateCompany) in ByFaction)
            {
                if (!string.Equals(candidateCompany, companyId, StringComparison.OrdinalIgnoreCase))
                    continue;

                faction = candidateFaction;
                return true;
            }
        }

        faction = string.Empty;
        return false;
    }
}
