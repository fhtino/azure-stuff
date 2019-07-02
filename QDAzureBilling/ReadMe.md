
# Quick & Dirty Azure Billing Helper

### AzureAD setup

 - Azure Active Directory --> App registration ...
 - Subscription --> IAM --> Add Role assignment
   - Role: Billing Reader
   - Select: AAD-Application_name


### Sample

Output from sample application (redacted data):

```
Last billing periods:
 > 05/06/2019 04/07/2019
 > 05/05/2019 04/06/2019
 > 05/04/2019 04/05/2019
 > 05/03/2019 04/04/2019
 > 05/02/2019 04/03/2019

Last period details:

Services:
 > azure app service : 100,00
 > bandwidth : 20,00
 > functions : 40,00
 * Total: 160,00

Day by day:
 > 05/06/2019 : 10,00
 > 06/06/2019 : 10,00
 > 07/06/2019 : 10,00
 > 08/06/2019 : 10,00
 > 09/06/2019 : 10,00
 > 10/06/2019 : 10,00
 > 11/06/2019 : 10,00
 > 12/06/2019 : 10,00
 > 13/06/2019 : 10,00
 > 14/06/2019 : 10,00
 > 15/06/2019 : 10,00
 > 16/06/2019 : 10,00
 > 17/06/2019 : 10,00
 > 18/06/2019 : 10,00
 > 19/06/2019 : 10,00
 > 20/06/2019 : 10,00
 * Total: 160,00
```

### Required NuGet packages
 - Microsoft.IdentityModel.Clients.ActiveDirectory  [5.1.0]
 - Newtonsoft.Json  [12.0.2]

## Json fragments


```
{
  "type": "Usage",
  "dataSet": {
    "granularity": "Daily",
    "aggregation": {
      "totalCost": {
        "name": "PreTaxCost",
        "function": "Sum"
      }
    },
    "sorting": [
      { "direction": "ascending", "name": "UsageDate" }
    ]
  },
  "timeframe": "Custom",
  "timePeriod": {
    "from": "2019-03-05T00:00:00+00:00",
    "to": "2019-06-04T23:59:59+00:00"
  }
}
```

```
"columns": [
    {
      "name": "PreTaxCost",
      "type": "Number"
    },
    {
      "name": "UsageDate",
      "type": "Number"
    },
    {
      "name": "Currency",
      "type": "String"
    }
  ],
  "rows": [
    [ 2.1821494314496439, 20190605, "EUR" ],
    [ 2.1812038141698542, 20190606, "EUR" ],
    [ 2.180906248956612, 20190607, "EUR" ],
```


```
{
 "id": "....",
 "name": "....",
 "type": "Microsoft.CostManagement/query",
 "location": null,
 "sku": null,
 "eTag": null,
 "properties": {
   "nextLink": null,
   "columns": [
     {"name": "PreTaxCost","type": "Number"},
     {"name": "ServiceName","type": "String"},
     {"name": "Currency","type": "String"}
   ],
   "rows": [
     [ 13.2836616, "azure app service", "EUR" ],
     [ 0.0848038570164, "bandwidth", "EUR" ],
     [ 0.0220121319942, "functions", "EUR" ],
```


## Reference and useful links

REST documentation  
- https://docs.microsoft.com/en-us/rest/api/cost-management/
- https://docs.microsoft.com/en-us/rest/api/consumption/
- https://docs.microsoft.com/en-us/rest/api/billing/

Specification 
- https://github.com/Azure/azure-rest-api-specs/tree/master/specification

Note: consumption API does not support all type of subscription



