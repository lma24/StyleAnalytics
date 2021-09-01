using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Caching;
using System.Threading;
using ThirdParty;

namespace Adv
{
    public class AdvertisementService
    {
        //private static MemoryCache cache = new MemoryCache("");
        private static MemoryCache cache = new MemoryCache("demoCache");
        private static Queue<DateTime> errors = new Queue<DateTime>();

        private Object lockObj = new Object();

        
        NoSqlAdvProvider objNoSqlAdvProvider = new NoSqlAdvProvider();
        Advertisement objAdvertisement = new Advertisement();
        int errorCount = 0;
        int retryCount = int.Parse(ConfigurationManager.AppSettings["RetryCount"]);

        // **************************************************************************************************
        // Loads Advertisement information by id
        // from cache or if not possible uses the "mainProvider" or if not possible uses the "backupProvider"
        // **************************************************************************************************
        // Detailed Logic:
        // 
        // 1. Tries to use cache (and retuns the data or goes to STEP2)
        //
        // 2. If the cache is empty it uses the NoSqlDataProvider (mainProvider), 
        //    in case of an error it retries it as many times as needed based on AppSettings
        //    (returns the data if possible or goes to STEP3)
        //
        // 3. If it can't retrive the data or the ErrorCount in the last hour is more than 10, 
        //    it uses the SqlDataProvider (backupProvider)
        public Advertisement GetAdvertisement_old(string id)
        {
            Advertisement adv = null;

            lock (lockObj)
            {
                // Use Cache if available
                adv = (Advertisement)cache.Get($"AdvKey_{id}");

                // Count HTTP error timestamps in the last hour
                while (errors.Count > 20) errors.Dequeue();
                int errorCount = 0;
                foreach (var dat in errors)
                {
                    if (dat > DateTime.Now.AddHours(-1))
                    {
                        errorCount++;
                    }
                }


                // If Cache is empty and ErrorCount<10 then use HTTP provider
                if ((adv == null) && (errorCount < 10))
                {
                    int retry = 0;
                    do
                    {
                        retry++;
                        try
                        {
                            var dataProvider = new NoSqlAdvProvider();
                            adv = dataProvider.GetAdv(id);
                        }
                        catch
                        {
                            Thread.Sleep(1000);
                            errors.Enqueue(DateTime.Now); // Store HTTP error timestamp              
                        }
                    } while ((adv == null) && (retry < int.Parse(ConfigurationManager.AppSettings["RetryCount"])));


                    if (adv != null)
                    {
                        cache.Set($"AdvKey_{id}", adv, DateTimeOffset.Now.AddMinutes(5));
                    }
                }


                // if needed try to use Backup provider
                if (adv == null)
                {
                    adv = SQLAdvProvider.GetAdv(id);

                    if (adv != null)
                    {
                        cache.Set($"AdvKey_{id}", adv, DateTimeOffset.Now.AddMinutes(5));
                    }
                }
            }
            return adv;
        }


        #region Refactor the AdvertisementService By Leena

        public Advertisement GetAdvertisement(string id)
        {
            try
            {
                lock (lockObj)
                {
                    errorCount = getHTTPErrorCount();

                    /* 1.First checking Cache is null or not. If not, retrieving the details from cache and 
                     * storing into the object of Advertisement Class*/

                    if (cache.Get($"AdvKey_{id}") != null)
                    {
                        objAdvertisement = (Advertisement)cache.Get($"AdvKey_{id}");


                        /* 2. If still can't retrieve the data then using the NoSqlDataProvider (mainProvider)*/

                        if (objAdvertisement == null && (errorCount < 10))
                        {
                            int retry = 0;
                            do
                            {
                                retry++;
                                objAdvertisement = objNoSqlAdvProvider.GetAdv(id);
                            } while ((objAdvertisement == null) && (retry < retryCount));
                        }
                        else
                        {
                            cache.Set($"AdvKey_{id}", objAdvertisement, DateTimeOffset.Now.AddMinutes(5));
                        }
                    }
                    else
                    {
                        /* 3.If cache is null, then retriving the data from SqlDataProvider (backupProvider) and 
                         * storing into the object of Advertisement Class*/
                        objAdvertisement = SQLAdvProvider.GetAdv(id);

                    }

                }
            }
            catch (Exception ex)
            {
                Thread.Sleep(1000);
                errors.Enqueue(DateTime.Now); // Store HTTP error timestamp   
                throw ex;
            }

            return objAdvertisement;
        }

        #region Getting Count HTTP error timestamps in the last hour

        private int getHTTPErrorCount()
        {
            try
            {
                while (errors.Count > 20)
                {
                    errors.Dequeue();
                    foreach (var dat in errors)
                    {
                        if (dat > DateTime.Now.AddHours(-1))
                        {
                            errorCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return errorCount;
        }

        #endregion

        #endregion

    }
}
