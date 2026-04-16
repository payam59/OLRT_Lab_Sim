using System.Text.Json;
using System.Text.RegularExpressions;

namespace OLRTLabSim.Helpers
{
    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var regex = new Regex("([a-z])([A-Z]+)");
            return regex.Replace(name, "$1_$2").ToLower();
        }
    }
}
