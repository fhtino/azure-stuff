using System;
using System.Collections.Generic;
using System.Text;


namespace QDAzureBilling
{
    public class TimePeriod
    {
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
    }

    public class PeriodCosts
    {
        public TimePeriod Period { get; set; }
        public List<ServiceCost> Details { get; set; }
    }

    public class ServiceCost
    {
        // example : 0.0, "azure active directory b2c", "EUR"
        public string ServiceName { get; set; }
        public double Value { get; set; }
        public string Currency { get; set; }
    }


    public class DailyCost
    {
        public DateTime DT { get; set; }
        public double Value { get; set; }
    }

}
