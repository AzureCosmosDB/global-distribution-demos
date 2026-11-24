using System;
using System.Collections.Generic;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;


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

}
