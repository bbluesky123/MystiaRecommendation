using System.Collections.Generic;
using System.IO;

namespace MystiaRecommendation.Engine;

public class CustomerDataEngine
{
    private readonly Dictionary<string, CustomerData> _customers = new();
    public int CustomerCount => _customers.Count;

    public CustomerDataEngine(string dataDirectory)
    {
        var filePath = Path.Combine(dataDirectory, "customers_rare.json");
        if (!File.Exists(filePath))
        {
            Plugin.Instance?.Log.LogError("文件不存在: " + filePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var arr = SimpleJson.ParseArray(json);

            foreach (var t in arr)
            {
                var c = new CustomerData();
                c.id = t.ContainsKey("id") ? SimpleJson.ToInt(t["id"]) : 0;
                c.name = t.ContainsKey("name") ? SimpleJson.ToString(t["name"]) : "";
                c.dlc = t.ContainsKey("dlc") ? SimpleJson.ToInt(t["dlc"]) : 0;
                c.enduranceLimit = t.ContainsKey("enduranceLimit") ? SimpleJson.ToDouble(t["enduranceLimit"]) : 1.0;
                c.price = t.ContainsKey("price") ? SimpleJson.ToIntList(t["price"]) : new();
                c.positiveTags = t.ContainsKey("positiveTags") ? SimpleJson.ToStringList(t["positiveTags"]) : new();
                c.negativeTags = t.ContainsKey("negativeTags") ? SimpleJson.ToStringList(t["negativeTags"]) : new();
                c.beverageTags = t.ContainsKey("beverageTags") ? SimpleJson.ToStringList(t["beverageTags"]) : new();
                c.places = t.ContainsKey("places") ? SimpleJson.ToStringList(t["places"]) : new();
                c.positiveTagMapping = t.ContainsKey("positiveTagMapping") ? ReadStringDictionary(t["positiveTagMapping"]) : new();
                c.beverageTagMapping = t.ContainsKey("beverageTagMapping") ? ReadStringDictionary(t["beverageTagMapping"]) : new();

                if (t.ContainsKey("spellCards") && t["spellCards"] is Dictionary<string, object> sc)
                {
                    c.spellCards = new SpellCardData();
                    if (sc.ContainsKey("positive") && sc["positive"] is List<object> posList)
                        foreach (var s in posList)
                            if (s is Dictionary<string, object> sd)
                                c.spellCards.positive.Add(new SpellCard
                                {
                                    name = SimpleJson.ToString(sd.GetValueOrDefault("name")),
                                    description = SimpleJson.ToString(sd.GetValueOrDefault("description"))
                                });
                    if (sc.ContainsKey("negative") && sc["negative"] is List<object> negList)
                        foreach (var s in negList)
                            if (s is Dictionary<string, object> sd)
                                c.spellCards.negative.Add(new SpellCard
                                {
                                    name = SimpleJson.ToString(sd.GetValueOrDefault("name")),
                                    description = SimpleJson.ToString(sd.GetValueOrDefault("description"))
                                });
                }

                if (!string.IsNullOrEmpty(c.name))
                    _customers[c.name] = c;
            }

            Plugin.Instance?.Log.LogInfo("成功加载 " + _customers.Count + " 个稀客数据");
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log.LogError("加载稀客数据失败: " + e.Message);
        }
    }

    public bool HasCustomer(string name) => _customers.ContainsKey(name);
    public CustomerData GetCustomer(string name) => _customers.TryGetValue(name, out var data) ? data : null;
    public IReadOnlyDictionary<string, CustomerData> GetAllCustomers() => _customers;

    private static Dictionary<string, string> ReadStringDictionary(object value)
    {
        var result = new Dictionary<string, string>();
        if (value is not Dictionary<string, object> dict) return result;

        foreach (var kv in dict)
        {
            var text = SimpleJson.ToString(kv.Value);
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(text))
                result[kv.Key] = text;
        }

        return result;
    }
}

public class CustomerData
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public int dlc { get; set; }
    public List<int> price { get; set; } = new();
    public double enduranceLimit { get; set; } = 1.0;
    public List<string> positiveTags { get; set; } = new();
    public List<string> negativeTags { get; set; } = new();
    public List<string> beverageTags { get; set; } = new();
    public List<string> places { get; set; } = new();
    public Dictionary<string, string> positiveTagMapping { get; set; } = new();
    public Dictionary<string, string> beverageTagMapping { get; set; } = new();
    public SpellCardData spellCards { get; set; }

    public int MaxBudget => price.Count >= 2
        ? (int)System.Math.Ceiling(price[1] * enduranceLimit)
        : 999;
}

public class SpellCardData
{
    public List<SpellCard> positive { get; set; } = new();
    public List<SpellCard> negative { get; set; } = new();
}

public class SpellCard
{
    public string name { get; set; } = "";
    public string description { get; set; } = "";
}
