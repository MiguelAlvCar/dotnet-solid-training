using AutoMapper;
using DevBasics.CarManagement.Dependencies;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static DevBasics.CarManagement.Dependencies.RegistrationApiResponseBase;

namespace DevBasics.CarManagement
{
    public class CarManagementService : BaseService
    {
        private readonly IMapper _mapper;

        public CarManagementService(
            IMapper mapper,
            CarManagementSettings settings,
            HttpHeaderSettings httpHeader,
            IKowoLeasingApiClient apiClient,
            ITransactionStateService transactionStateService,
            IBulkRegistrationService bulkRegisterService,
            IRegistrationDetailService registrationDetailService,
            ILeasingRegistrationRepository registrationRepository,
            ICarRegistrationRepository carRegistrationRepository)
                : base(settings, httpHeader, apiClient,
                      transactionStateService: transactionStateService,
                      bulkRegistrationService: bulkRegisterService,
                      registrationDetailService: registrationDetailService,
                      leasingRegistrationRepository: registrationRepository,
                      carLeasingRepository: carRegistrationRepository)
        {
            Console.WriteLine($"Initializing service {nameof(CarManagementService)}");

            _mapper = mapper;
        }

        public bool HasMissingData(CarRegistrationModel car)
        {
            return (string.IsNullOrWhiteSpace(car.CompanyId))
                        || (string.IsNullOrWhiteSpace(car.VehicleIdentificationNumber))
                            || (string.IsNullOrWhiteSpace(car.CustomerId))
                                || car.DeliveryDate == null
                                    || (string.IsNullOrWhiteSpace(car.ErpDeliveryNumber));
        }

        internal async Task<BulkRegistrationRequest> MapToModel(RegistrationType registrationType, RegisterCarsModel cars, string transactionId)
        {
            BulkRegistrationRequest requestModel = new BulkRegistrationRequest();
            List<DeliveryRequest> deliveryModels = new List<DeliveryRequest>();

            try
            {
                requestModel.RequestContext = await base.InitializeRequestContextAsync();

                requestModel.TransactionId = transactionId;
                requestModel.CompanyId = cars.CompanyId;
                requestModel.Registrations = new List<RegistrationRequest>();

                IEnumerable<IGrouping<string, CarRegistrationModel>> groups = cars.Cars.GroupBy(x => x.CarPoolNumber);

                foreach (IGrouping<string, CarRegistrationModel> registration in groups)
                {
                    DateTime registrationDate = registration.Min(item => item.RegistrationDate.GetValueOrDefault());
                    string convertedRegistrationDate = $"{registrationDate.Year}-{registrationDate.Month}-{registrationDate.Day}T{registrationDate.Hour:00}:{registrationDate.Minute:00}:{registrationDate.Second:00}Z";

                    if (registrationType != RegistrationType.Reset)
                    {
                        deliveryModels = GetDeliveryGroups(registration).ToList();
                    }

                    requestModel.Registrations.Add(new RegistrationRequest
                    {
                        RegistrationNumber = registration.Key,
                        CustomerId = cars.CustomerId,
                        RegistrationDate = convertedRegistrationDate,
                        RegistrationType = registrationType.ToString(),
                        Deliveries = deliveryModels
                    });
                }

                Console.WriteLine($"Mapping from registration to request model successful. Data (serialized as JSON): {JsonConvert.SerializeObject(requestModel)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Mapping registration to request model failed. Data (serialized as JSON): {JsonConvert.SerializeObject(cars)}: {ex}");

                throw ex;
            }

            return requestModel;
        }

        internal ServiceResult MapToModel(
            ServiceResult serviceResult, BulkRegistrationResponse registrationResponse,
            string internalTransactionId, IList<int> identifier, string registrationId = "n/a")
        {
            serviceResult.TransactionId = internalTransactionId;
            serviceResult.RegistrationId = registrationId;
            serviceResult.RegisteredCarIds = identifier.ToList();
            serviceResult.TransactionState = "n/a";

            if (registrationResponse != null)
            {
                if (registrationResponse.RegistrationId != null)
                {
                    serviceResult.Message = "SUCCESS";
                }
                else if (registrationResponse.TransactionId != null || registrationResponse.Errors?.Count > 0)
                {
                    serviceResult.Message = "ERROR";
                }
            }
            else
            {
                serviceResult.Message = "ERROR";
            }

            return serviceResult;
        }

        private IList<DeliveryRequest> GetDeliveryGroups(IEnumerable<CarRegistrationModel> cars)
        {
            List<DeliveryRequest> deliveryGroups = new List<DeliveryRequest>();

            List<List<IGrouping<string, CarRegistrationModel>>> groupedCars = cars
                .GroupBy(x => x.DeliveryDate)
                .Select(grp => grp.ToList())
                .ToList()
                .Select(y => y.ToList().GroupBy(z => z.ErpDeliveryNumber))
                .Select(grp => grp.ToList())
                .ToList();

            foreach (List<IGrouping<string, CarRegistrationModel>> group in groupedCars)
            {
                foreach (IGrouping<string, CarRegistrationModel> carList in group)
                {
                    List<CarRequest> carsOfGroup = new List<CarRequest>();
                    foreach (CarRegistrationModel car in carList)
                    {
                        carsOfGroup.Add(new CarRequest() { VehicleIdentificationNumber = car.VehicleIdentificationNumber, AssetTag = string.Empty });
                    }

                    DateTime deliveryDate = carList.FirstOrDefault().DeliveryDate.Value;
                    string convertedDeliveryDate = string.Format("{0}-{1}-{2}T{3:00}:{4:00}:{5:00}Z",
                        deliveryDate.Year, deliveryDate.Month, deliveryDate.Day, deliveryDate.Hour, deliveryDate.Minute, deliveryDate.Second);

                    deliveryGroups.Add(new DeliveryRequest()
                    {
                        DeliveryNumber = carList.FirstOrDefault().ErpDeliveryNumber,
                        DeliveryDate = convertedDeliveryDate,
                        Cars = carsOfGroup
                    });
                }
            }

            return deliveryGroups;
        }

        internal async Task<string> BeginTransactionGenerateId(IList<string> cars,
            string customerId, string companyId, RegistrationType registrationType, string identity, string registrationNumber = null)
        {
            Console.WriteLine(
                $"Trying to generate internal database transaction and initialize the transaction. Cars: {string.Join(",  ", cars)} ");

            try
            {
                string transactionId = DateTime.Now.Ticks.ToString();
                if (transactionId.Length > 32)
                {
                    transactionId = transactionId.Substring(0, 32);
                }

                return await BeginTransactionAsync(cars, customerId, companyId, registrationType, identity, transactionId, registrationNumber);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Generating internal Transaction ID and initializing transaction failed. Cars: {string.Join(", ", cars)}: {ex}");

                throw ex;
            }
        }

        private async Task<string> BeginTransactionAsync(IList<string> cars,
            string customerId, string companyId, RegistrationType registrationType, string identity,
            string transactionId = null, string registrationNumber = null)
        {
            Console.WriteLine(
                $"Trying to begin internal database transaction. Cars: {string.Join(",  ", cars)}");

            try
            {
                IList<CarRegistrationDto> dbCarsToUpdate = await CarLeasingRepository.GetCarsAsync(cars);
                foreach (CarRegistrationDto carToUpdate in dbCarsToUpdate)
                {
                    if (!string.IsNullOrWhiteSpace(transactionId))
                    {
                        carToUpdate.TransactionId = transactionId;
                    }

                    if (!string.IsNullOrWhiteSpace(registrationNumber))
                    {
                        carToUpdate.CarPoolNumber = registrationNumber;
                    }

                    carToUpdate.TransactionEndDate = null;
                    carToUpdate.ErrorMessage = string.Empty;
                    carToUpdate.ErrorCode = null;

                    carToUpdate.TransactionType = (int)registrationType;
                    //carToUpdate.TransactionState = (int)TransactionResult.Progress;
                    carToUpdate.TransactionState = carToUpdate.TransactionState ?? (int)TransactionResult.NotRegistered;

                    Console.WriteLine(
                        $"Car hasn't got missing data. Setting status to {carToUpdate.TransactionState}");

                    carToUpdate.TransactionStartDate = DateTime.Now;

                    Console.WriteLine(
                        $"Trying to update car {carToUpdate.CarIdentificationNumber} in database...");

                    await LeasingRegistrationRepository.UpdateCarAsync(carToUpdate);
                    await LeasingRegistrationRepository.InsertHistoryAsync(carToUpdate,
                        identity,
                        ((carToUpdate.TransactionState.HasValue) ? Enum.GetName(typeof(TransactionResult), (int)carToUpdate.TransactionState) : null),
                        ((carToUpdate.TransactionType.HasValue) ? Enum.GetName(typeof(RegistrationType), (int)carToUpdate.TransactionType) : null)
                    );
                }

                Console.WriteLine(
                        $"Beginning internal database transaction ended. Cars: {string.Join(",  ", cars)}, " +
                        $"Returning internal Transaction ID: {transactionId}");

                return transactionId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Beginning internal database transaction failed. Cars: {string.Join(",  ", cars)}: {ex}");

                throw new Exception("Beginning internal database transaction failed", ex);
            }
        }

        internal async Task<IList<int>> FinishTransactionAsync(RegistrationType registrationType,
            BulkRegistrationResponse apiResponse, IList<string> carIdentifier, string companyId, string identity,
            string transactionStateBackup = null, BulkRegistrationRequest requestModel = null)
        {
            Console.WriteLine($"Trying to finish database transaction after bulk registration (Type {registrationType.ToString()})...");

            List<int> updateResult = new List<int>();

            try
            {
                // Get the cars from database.
                IList<CarRegistrationDto> dbCars = await CarLeasingRepository.GetCarsAsync(carIdentifier);
                foreach (CarRegistrationDto dbCar in dbCars)
                {
                    Console.WriteLine($"Now processing car {dbCar.RegisteredCarId}...");

                    dbCar.TransactionType = (int)registrationType;
                    dbCar.CompanyId = companyId;

                    TransactionResult newTransactionState = await GetTransactionResult(apiResponse, dbCar, registrationType, transactionStateBackup);
                    string parsedTransactionStateBackup = Enum.GetName(typeof(TransactionResult), (!string.IsNullOrWhiteSpace(transactionStateBackup))
                                                        ? int.Parse(transactionStateBackup)
                                                        : (int)TransactionResult.None);

                    Console.WriteLine(
                        $"Initial new transaction status: {newTransactionState.ToString()}, Backup old transaction status: {parsedTransactionStateBackup}");

                    if (apiResponse != null)
                    {
                        if ((newTransactionState.ToString() == parsedTransactionStateBackup && apiResponse.Response != "SUCCESS")
                                || newTransactionState == TransactionResult.Failed
                                    || (newTransactionState == TransactionResult.NotRegistered && dbCar.TransactionType != (int)RegistrationType.Unregister))
                        {
                            Console.WriteLine(
                                $"An error occured or the transaction could not be processed (new transaction status is the old transaction status from car logs)." +
                                $"Closing the transaction");

                            Tuple<string, string, string> errorValues = GetErrorValues(apiResponse);
                            if (errorValues != null)
                            {
                                dbCar.ErrorCode = errorValues.Item1;
                                dbCar.ErrorMessage = errorValues.Item2;
                                dbCar.AccTransactionId = errorValues.Item3;
                            }

                            // if an error occurred or the transaction could not be processed (new transaction state is the old transaction state)
                            // close the transaction.
                            dbCar.TransactionState = (newTransactionState != TransactionResult.None) ? (int?)newTransactionState : null;
                            dbCar.TransactionEndDate = DateTime.Now;
                        }
                        else
                        {
                            Console.WriteLine(
                                $"Set car {dbCar.CarIdentificationNumber} to status {TransactionResult.Progress.ToString()} " +
                                $"and ACC Transaction ID {apiResponse.RegistrationId}");

                            dbCar.TransactionState = (int)TransactionResult.Progress;
                            dbCar.AccTransactionId = apiResponse.RegistrationId;
                        }
                    }

                    Console.WriteLine(
                        $"Trying to update car {dbCar.CarIdentificationNumber} in database...");

                    int result = await CarLeasingRepository.UpdateRegisteredCarAsync(dbCar, identity);

                    if (result != -1)
                    {
                        updateResult.Add(dbCar.RegisteredCarId);
                    }
                }

                Console.WriteLine($"Trying to finish database transaction after bulk registration (Type {registrationType.ToString()}) ended.");

                return updateResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Finishing database transaction after bulk registration (Type {registrationType.ToString()}) failed: {ex}");

                throw new Exception("Finishing database transaction after bulk registration failed", ex);
            }
        }

        private async Task<TransactionResult> GetTransactionResult(BulkRegistrationResponse apiResponse,
            CarRegistrationDto dbCar, RegistrationType registrationType, string transactionStateBackup)
        {
            Console.WriteLine($"Trying to get the transaction result for car {dbCar.CarIdentificationNumber}...");

            try
            {
                Enum.TryParse(transactionStateBackup, out TransactionResult oldTxState);
                if (apiResponse == null)
                {
                    if (transactionStateBackup != null)
                    {
                        return oldTxState;
                    }
                    else
                    {
                        if (registrationType == RegistrationType.Register)
                        {
                            return TransactionResult.NotRegistered;
                        }
                        else
                        {
                            return TransactionResult.Failed;
                        }

                    }
                }

                switch (registrationType)
                {
                    case RegistrationType.Register:
                        if (apiResponse.RegistrationId != null
                                && apiResponse.Response != null
                                    && apiResponse.Response == "SUCCESS")
                        {
                            return TransactionResult.Registered;
                        }
                        else if (apiResponse.TransactionId != null || apiResponse.Errors != null)
                        {
                            Console.WriteLine("API responded with an error. Now checking if the car was registered the first time or subsequent...");
                            if (await IsFirstTransaction(dbCar.CarIdentificationNumber, dbCar.RegistrationId))
                            {
                                // if the car was imported the first time, set the state to error
                                Console.WriteLine($"Car was imported the first time. Returning transaction Result: {TransactionResult.NotRegistered.ToString()}");

                                return TransactionResult.NotRegistered;
                            }
                            else
                            {
                                Console.WriteLine(
                                    $"Car was tried to be registered with subsequent Registration-Transaction. " +
                                    $"Returning the transaction result as it was before it the process started: {oldTxState.ToString()}");

                                return oldTxState;
                            }
                        }
                        break;

                    case RegistrationType.Unregister:
                        Console.WriteLine("Trying to analyze unregistration transaction result...");
                        if (!await IsFirstTransaction(dbCar.CarIdentificationNumber, dbCar.RegistrationId))
                        {
                            if (apiResponse.RegistrationId != null)
                            {
                                return TransactionResult.NotRegistered;
                            }
                            else if (apiResponse.TransactionId != null || apiResponse.Errors != null)
                            {
                                return oldTxState;
                            }
                        }
                        break;

                    case RegistrationType.Override:
                    case RegistrationType.Reset:
                        if (apiResponse.RegistrationId != null)
                        {
                            return TransactionResult.Progress;
                        }
                        else if (apiResponse.TransactionId != null
                                    || apiResponse.Errors != null)
                        {
                            return oldTxState;
                        }
                        break;

                    default:
                        Console.WriteLine($"BulkRegistrationType not valid. Transaction result cannot be determined.");
                        break;
                }

                // If the algorithm executes to this point no transaction state change.
                Console.WriteLine($"Could not determine new transaction result. Transaction state before process was initiated is returned:  {oldTxState}");

                return oldTxState;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Getting transaction result for database by API response failed: {ex}");

                throw new Exception("Getting transaction result for database by API response failed", ex);
            }
        }

        private Tuple<string, string, string> GetErrorValues(BulkRegistrationResponse apiResponse)
        {
            // In case of TransactionId contains a value, a Ship-To-Error occured and only one error message was received.
            if (apiResponse.TransactionId != null)
            {
                return Tuple.Create(apiResponse.ErrorCode, apiResponse.ErrorMessage, apiResponse.TransactionId);
            }
            else if (apiResponse.Errors != null)
            {
                if (apiResponse.Errors.Count > 1)
                {
                    return Tuple.Create("MULTI", string.Join(" // ", apiResponse.Errors), apiResponse.RegistrationId);
                }
                else if (apiResponse.Errors.Count == 1)
                {
                    return Tuple.Create(apiResponse.Errors.FirstOrDefault(), apiResponse.Errors.FirstOrDefault(), string.Empty);
                }
            }

            return null;
        }

        private async Task<bool> IsFirstTransaction(string carIdentificationNumber, string registrationRegistrationId)
        {
            Console.WriteLine($"Trying to analyze if this is the first transaction for car {carIdentificationNumber}...");

            IEnumerable<CarRegistrationLogDto> carHistory = (await CarLeasingRepository.GetCarHistoryAsync(carIdentificationNumber)).Where(x => x.RegistrationId == registrationRegistrationId);

            if (carHistory != null)
            {
                IOrderedEnumerable<CarRegistrationLogDto> sortedCarHistory = carHistory.OrderBy(d => d.RowCreationDate);
                bool isInitialTransaction = (!sortedCarHistory.Any(x => x.TransactionState == TransactionResult.Registered.ToString()));

                Console.WriteLine($"History of car {carIdentificationNumber} is not null, returning {isInitialTransaction}");

                return isInitialTransaction;
            }
            else
            {
                Console.WriteLine($"History of car {carIdentificationNumber} is null, returning true");
                return true;
            }
        }

        internal async Task HandleDataRevertAsync(List<int> dbCarsToRevert, string identity, bool onlyForceRegistermentItems = true)
        {
            Console.WriteLine($"Trying to execute data revert of cars. " +
                $"Cars (serialized as JSON): {JsonConvert.SerializeObject(dbCarsToRevert)}, " +
                $"Azure User Identity: {identity}," +
                $"Only Force Registerment Items: {onlyForceRegistermentItems}");

            try
            {
                if (dbCarsToRevert != null && dbCarsToRevert.Count > 0)
                {
                    foreach (int id in dbCarsToRevert)
                    {
                        // Get all history items of car which should be reverted.
                        IEnumerable<CarRegistrationLogDto> carHistory = await CarLeasingRepository.GetCarHistoryAsync(id.ToString());

                        DateTime? rowCreationDate = null;
                        if (onlyForceRegistermentItems)
                        {
                            Console.WriteLine($"Get latest row creation date of car with ID {id} of 'Force Registerment User'");

                            // Extract the RowCreationDate of the item which contains the data to revert the car.
                            rowCreationDate = carHistory?
                                .Where(x => x.UserName != "Force Registerment User" && (string.IsNullOrWhiteSpace(x.TransactionType) || x.TransactionType != TransactionResult.Progress.ToString()))
                                .FirstOrDefault()
                                .RowCreationDate;
                        }
                        else
                        {
                            rowCreationDate = carHistory?
                                .Where(x => (string.IsNullOrWhiteSpace(x.TransactionType) || x.TransactionType != TransactionResult.Progress.ToString()))
                                .FirstOrDefault()
                                .RowCreationDate;
                        }

                        // Call method to revert the car.
                        bool isReverted = await RevertCarDataAsync(id, rowCreationDate, identity);

                        Console.WriteLine($"Revert completed with result: {isReverted}");
                    }
                }
                else
                {
                    Console.WriteLine($"List of cars to revert is empty. Revert not possible");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Unexpected error while handling data revert of cars: Revert failed." +
                    $"Cars (serialized as JSON): {JsonConvert.SerializeObject(dbCarsToRevert)}, " +
                    $"Azure User Identity: {identity}," +
                    $"Only Force Registerment Items: {onlyForceRegistermentItems}: {ex}");
            }
        }

        private async Task<bool> RevertCarDataAsync(int carDatasetId, DateTime? rowCreationDate, string identity)
        {
            Console.WriteLine($"Trying to revert car data to values of latest history item of car with ID {carDatasetId}");

            try
            {
                if (rowCreationDate != null)
                {

                    CarRegistrationModel currentCarData = await CarLeasingRepository.GetApiRegisteredCarAsync(carDatasetId);

                    var car = (await CarLeasingRepository.GetCarHistoryAsync(carDatasetId.ToString()))
                        .Where(x => x.RowCreationDate == rowCreationDate)
                        .FirstOrDefault();

                    CarRegistrationModel latestCarHistoryData = new CarRegistrationModel
                    {
                        RegistrationId = car.RegistrationId
                    };

                    Console.WriteLine(
                        $"Current car data (serialized as JSON): {JsonConvert.SerializeObject(currentCarData)}\n" +
                        $"Latest history data (serialized as JSON): {JsonConvert.SerializeObject(latestCarHistoryData)}");

                    latestCarHistoryData.ErrorNotificationSent = null;
                    AssignCarValuesForUpdate(currentCarData, latestCarHistoryData, identity, true);

                    return true;
                }
                else
                {
                    Console.WriteLine($"RowCreationDate is null, revert item cannot be identified. Aborting revert");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error while reading current data with id {carDatasetId} and revert item of date {rowCreationDate}: {ex}");
            }

            return false;
        }

        internal void AssignCarValuesForUpdate(CarRegistrationModel carToUpdate, CarRegistrationModel carUpdateValues, string identity, bool saveWithHistory = false, string source = null)
        {
            Console.WriteLine($"Trying to assign values from revert item." +
                $"Car (serialized as JSON): {carToUpdate}, " +
                $"Revert Data (serialized as JSON): {carUpdateValues}");

            try
            {
                carToUpdate.ErpRegistrationNumber = carUpdateValues.ErpRegistrationNumber;
                carToUpdate.CompanyId = carUpdateValues.CompanyId;
                carToUpdate.CustomerId = carUpdateValues.CustomerId;
                carToUpdate.EmailAddresses = carUpdateValues.EmailAddresses;
                carToUpdate.CustomerRegistrationReference = carUpdateValues.CustomerRegistrationReference;
                carToUpdate.CarPool = carUpdateValues.CarPool;
                carToUpdate.ErrorNotificationSent = carUpdateValues.ErrorNotificationSent;

                carToUpdate.Source = source ?? carUpdateValues.Source;

                if (!string.IsNullOrWhiteSpace(carUpdateValues.CarPoolNumber))
                {
                    carToUpdate.CarPoolNumber = carUpdateValues.CarPoolNumber;
                }

                CarLeasingRepository.UpdateErpRegistrationItemAsync(_mapper.Map<ErpRegistermentRegistration>(carToUpdate));

                CarRegistrationDto dbCar = _mapper.Map<CarRegistrationDto>(carToUpdate);

                // Mapping ignores these two properties, so the values have to be set manually.
                Enum.TryParse(carToUpdate.TransactionType, out RegistrationType parsedTransactionType);
                Enum.TryParse(carToUpdate.TransactionState, out TransactionResult parsedTransactionStatus);
                dbCar.TransactionType = (int)parsedTransactionType;
                dbCar.TransactionState = (int)parsedTransactionStatus;

                CarLeasingRepository.UpdateRegisteredCarAsync(dbCar, identity, saveWithHistory);

                Console.WriteLine($"Reverted car data. Car (serialized as JSON): {JsonConvert.SerializeObject(dbCar)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Unexpected error while assingning values from revert item. " +
                    $"Car (serialized as JSON): {carToUpdate}, " +
                    $"Revert Data (serialized as JSON): {carUpdateValues}: {ex}");
            }
        }
    }
}
