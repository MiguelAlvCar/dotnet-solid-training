using DevBasics.CarManagement.Dependencies;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    public class BaseService
    {
        protected IGetAppSetting _getAppSetting;
        protected IUpdateCar _updateCar;
        protected IInsertHistory _insertHistory;

        public CarManagementSettings Settings { get; set; }

        public HttpHeaderSettings HttpHeader { get; set; }

        public IBulkRegistrationService BulkRegistrationService { get; set; }

        public ICarRegistrationRepository CarLeasingRepository { get; set; }

        public BaseService(
            CarManagementSettings settings,
            HttpHeaderSettings httpHeader,
            IBulkRegistrationService bulkRegistrationService = null,
            IGetAppSetting getAppSetting = null,
            IUpdateCar updateCar = null,
            IInsertHistory insertHistory = null,
            ICarRegistrationRepository carLeasingRepository = null)
        {
            // Mandatory
            Settings = settings;
            HttpHeader = httpHeader;

            // Optional Services
            BulkRegistrationService = bulkRegistrationService;

            // Optional Repositories
            _getAppSetting = getAppSetting;
            CarLeasingRepository = carLeasingRepository;
        }

        public async Task<RequestContext> InitializeRequestContextAsync()
        {
            Console.WriteLine("Trying to initialize request context...");

            try
            {
                AppSettingDto settingResult = await _getAppSetting.GetAppSettingAsync(HttpHeader.SalesOrgIdentifier, HttpHeader.WebAppType);

                if (settingResult == null)
                {
                    throw new Exception("Error while retrieving settings from database");
                }

                RequestContext requestContext = new RequestContext()
                {
                    ShipTo = settingResult.SoldTo,
                    LanguageCode = Settings.LanguageCodes["English"],
                    TimeZone = "Europe/Berlin"
                };

                Console.WriteLine($"Initializing request context successful. Data (serialized as JSON): {JsonConvert.SerializeObject(requestContext)}");

                return requestContext;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Initializing request context failed: {ex}");
                return null;
            }
        }
    }
}
