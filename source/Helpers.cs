using System;
using System.Collections.Generic;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using NetFwTypeLib;
using System.Management.Automation;


namespace CosmosGlobalDistDemosCore
{
    static class Helpers
    {
        public static string Line = new string('-', 73);

        private static readonly DefaultAzureCredential Credential = new DefaultAzureCredential();

        public static CosmosClient CreateClient(string endpoint, string region) =>
            new CosmosClient(endpoint, Credential, new CosmosClientOptions { ApplicationRegion = region });

    }

    public class SampleCustomer
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "city")]
        public string City { get; set; }

        [JsonProperty(PropertyName = "postalcode")]
        public string PostalCode { get; set; }

        [JsonProperty(PropertyName = "region")]
        public string Region { get; set; }

        [JsonProperty(PropertyName = "myPartitionKey")]
        public string MyPartitionKey { get; set; }

        [JsonProperty(PropertyName = "userDefinedId")]
        public int UserDefinedId { get; set; }

        public static List<SampleCustomer> GenerateCustomers(string partitionKeyValue, int number)
        {
            //Generate fake customer data.
            Bogus.Faker<SampleCustomer> customerGenerator = new Bogus.Faker<SampleCustomer>().Rules((faker, customer) =>
            {
                customer.Id = Guid.NewGuid().ToString();
                customer.Name = faker.Name.FullName();
                customer.City = faker.Person.Address.City.ToString();
                customer.Region = faker.Person.Address.State.ToString();
                customer.PostalCode = faker.Person.Address.ZipCode.ToString();
                customer.MyPartitionKey = partitionKeyValue;
                customer.UserDefinedId = faker.Random.Int(0, 1000);
            });

            return customerGenerator.Generate(number);
        }
    }

    public class Firewall
    {

        public static void AddFirewallRule(string ruleName, string ipAddress)
        {
            try
            {
                INetFwRule firewallRule = (INetFwRule)Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwRule"));
                firewallRule.Action = NET_FW_ACTION_.NET_FW_ACTION_BLOCK;
                firewallRule.Description = "Block Multi-master";
                firewallRule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_OUT;
                firewallRule.Enabled = true;
                firewallRule.InterfaceTypes = "All";
                firewallRule.Name = ruleName;
                //firewallRule.Protocol = 6;// NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP;
                firewallRule.RemoteAddresses = ipAddress;

                INetFwPolicy2 firewallPolicy = (INetFwPolicy2)Activator.CreateInstance(
                    Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
                firewallPolicy.Rules.Add(firewallRule);

            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
        }

        public static void AddFirewallRulePS(string ruleName, string ipAddress)
        {
            
            PowerShell ps = PowerShell.Create();

            string command = $"New-NetFirewallRule -DisplayName {ruleName} -Action Block -Direction Outbound -Enabled True -InterfaceType Any -RemoteAddress '{ipAddress}'";

            ps.AddScript(command).Invoke();
        }

        public static void RemoveFirewallRule(string ruleName)
        {
            try
            { 
                INetFwPolicy2 firewallPolicy = (INetFwPolicy2)Activator.CreateInstance(
                        Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
                firewallPolicy.Rules.Remove(ruleName);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
        }

        public static void RemoveFirewallRulePS(string ruleName)
        {
            PowerShell ps = PowerShell.Create();

            string command = $"Remove-NetFirewallRule -DisplayName MultiMasterFailover {ruleName}";

            ps.AddScript(command).Invoke();
        }

    }

}
