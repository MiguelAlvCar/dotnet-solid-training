using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DevBasics.CarManagement.Dependencies;
using Newtonsoft.Json;
using static DevBasics.CarManagement.Dependencies.RegistrationApiResponseBase;

namespace DevBasics.CarManagement
{
    internal class CarRegistrationService
    {
        private readonly CarManagementService _carManagementService;

        private readonly CarRegistrationRepository _carRegistrationRepository;

        internal CarRegistrationService(CarManagementService carManagementService, CarRegistrationRepository carRegistrationRepository)
        {
            _carManagementService = carManagementService;
            _carRegistrationRepository = carRegistrationRepository;
        }        

        public async Task<ServiceResult> RegisterCarsAsync(RegisterCarsModel registerCarsModel, bool isForcedRegistration, Claims claims, string identity = "Unknown")
        {
            ServiceResult serviceResult = new ServiceResult();

            try
            {
                // See Feature 307.
                registerCarsModel.Cars.ToList().ForEach(x =>
                {
                    if (string.IsNullOrWhiteSpace(x.VehicleIdentificationNumber) == false)
                    {
                        x.VehicleIdentificationNumber = x.VehicleIdentificationNumber.ToUpper();
                    }
                });

                registerCarsModel.Cars = registerCarsModel.Cars.RemoveDuplicates();

                Console.WriteLine($"Trying to invoke initial bulk registration for {registerCarsModel.Cars.Count} cars. " +
                    $"Cars: {string.Join(", ", registerCarsModel.Cars.Select(x => x.VehicleIdentificationNumber))}, " +
                    $"Is forced registration: {isForcedRegistration}");

                if (isForcedRegistration && !registerCarsModel.DeactivateAutoRegistrationProcessing)
                {
                    List<CarRegistrationModel> existingItems = registerCarsModel.Cars.Where(x => x.IsExistingVehicleInAzureDB).ToList();
                    List<CarRegistrationModel> notExistingItems = registerCarsModel.Cars.Where(x => !x.IsExistingVehicleInAzureDB).ToList();

                    ServiceResult forceResponse = await ForceBulkRegistration(existingItems, "Force Registerment User");

                    if (forceResponse.Message.Contains("ERROR") || notExistingItems.Count == 0)
                    {
                        return forceResponse;
                    }
                    else
                    {
                        registerCarsModel.Cars = notExistingItems;
                    }
                }

                var toyota = new Toyota();
                toyota.GenerateRegistration(registerCarsModel.Cars.FirstOrDefault().CarPool,
                    out string registrationId,
                    out string carPoolNumber);

                Console.WriteLine($"Created unique car pool number {carPoolNumber} and registration id {registrationId}");

                DateTime today = DateTime.Now.Date;
                foreach (CarRegistrationModel car in registerCarsModel.Cars)
                {
                    car.CarPoolNumber = carPoolNumber;
                    car.RegistrationId = registrationId;

                    // See Bug 281.
                    if (string.IsNullOrWhiteSpace(car.ErpRegistrationNumber))
                    {
                        // See Feature 182.
                        if (car.DeliveryDate == null)
                        {
                            DateTime delivery = today.AddDays(-1);
                            car.DeliveryDate = delivery;
                        }

                        // See Feature 182.
                        if (string.IsNullOrWhiteSpace(car.ErpDeliveryNumber))
                        {
                            car.ErpDeliveryNumber = registrationId;

                            Console.WriteLine($"Car {car.VehicleIdentificationNumber} has no value for Delivery Number: Setting default value to registration id {registrationId}");
                        }
                    }

                    bool hasMissingData = _carManagementService.HasMissingData(car);
                    if (hasMissingData)
                    {
                        Console.WriteLine($"Car {car.VehicleIdentificationNumber} has missing data. " +
                            $"Set to transaction status {TransactionResult.MissingData.ToString()}");

                        car.TransactionState = TransactionResult.MissingData.ToString("D");
                    }
                }

                registerCarsModel.VendorId = registerCarsModel.Cars.Select(x => x.CompanyId).FirstOrDefault();
                registerCarsModel.CompanyId = registerCarsModel.VendorId;
                registerCarsModel.CustomerId = registerCarsModel.Cars.FirstOrDefault().CustomerId;

                RegisterCarsResult registrationResult = await new RegistrationService().SaveRegistrations(
                    registerCarsModel, claims, registrationId, identity, isForcedRegistration, CarBrand.Toyota);

                if (registerCarsModel.Cars.Any(x => x.TransactionState == TransactionResult.MissingData.ToString("D") == true)
                        && registerCarsModel.Cars.All(x => x.TransactionState == TransactionResult.MissingData.ToString("D")) == false)
                {
                    registerCarsModel.Cars = registerCarsModel
                        .Cars
                        .Where(x => x.TransactionState != TransactionResult.MissingData.ToString("D"))
                        .ToList();
                }

                if (registrationResult.AlreadyRegistered)
                {
                    serviceResult.Message = TransactionHelper.ALREADY_ENROLLED;
                    return serviceResult;
                }

                if (registrationResult.RegisteredCars != null && registrationResult.RegisteredCars.Count > 0)
                {
                    Console.WriteLine(
                        $"Registering {registrationResult.RegisteredCars.Count} cars for registration with id {registrationResult.RegistrationId}. " +
                        $"(RegistrationId = {registrationId})");

                    bool hasMissingData = _carManagementService.HasMissingData(registerCarsModel.Cars.FirstOrDefault());

                    string transactionId = await _carManagementService.BeginTransactionGenerateId(
                                            registerCarsModel.Cars.Select(x => x.VehicleIdentificationNumber).ToList(),
                                            registerCarsModel.CustomerId,
                                            registerCarsModel.CompanyId,
                                            RegistrationType.Register,
                                            identity);

                    if (!hasMissingData)
                    {
                        BulkRegistrationRequest requestPayload = null;
                        BulkRegistrationResponse apiTransactionResult = null;
                        try
                        {
                            requestPayload = await _carManagementService.MapToModel(RegistrationType.Register, registerCarsModel, transactionId);
                            apiTransactionResult = await _carManagementService.BulkRegistrationService.ExecuteRegistrationAsync(requestPayload);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Registering cars for registration with id {registrationResult.RegistrationId} (RegistrationId = {registrationId}) failed. " +
                                $"Database transaction will be finished anyway: {ex}");
                        }

                        IList<int> identifier = await _carManagementService.FinishTransactionAsync(RegistrationType.Register,
                            apiTransactionResult,
                            registrationResult.RegisteredCars,
                            registerCarsModel.CompanyId,
                            identity);

                        // Mapping to model that is excpected by the UI.
                        serviceResult = _carManagementService.MapToModel(serviceResult,
                            apiTransactionResult,
                            requestPayload?.TransactionId,
                            identifier,
                            registrationId);
                    }
                    else
                    {
                        Console.WriteLine($"Car has missing data. Trying to set transaction status to {TransactionResult.MissingData}");

                        IEnumerable<IGrouping<string, CarRegistrationModel>> group = registerCarsModel.Cars.GroupBy(x => x.RegistrationId);
                        foreach (IGrouping<string, CarRegistrationModel> grp in group)
                        {
                            IList<CarRegistrationModel> dbApiCars = await _carManagementService.CarLeasingRepository.GetApiRegisteredCarsAsync(grp.Key);
                            foreach (CarRegistrationModel dbApiCar in dbApiCars)
                            {
                                CarRegistrationDto dbCar = new CarRegistrationDto
                                {
                                    RegistrationId = dbApiCar.RegistrationId
                                };

                                dbCar.TransactionState = (int?)TransactionResult.MissingData;
                                await _carManagementService.CarLeasingRepository.UpdateRegisteredCarAsync(dbCar, identity);

                                Console.WriteLine($"Updated car {dbApiCar.VehicleIdentificationNumber} to database. " +
                                    $"Car (serialized as JSON): {JsonConvert.SerializeObject(dbApiCar)}");
                            }
                        }

                        serviceResult.RegistrationId = registrationResult.RegistrationId;
                        serviceResult.Message = TransactionResult.MissingData.ToString();

                        Console.WriteLine($"Processing of bulk registration ended. Return data (serialized as JSON): {JsonConvert.SerializeObject(serviceResult)}");

                        return serviceResult;
                    }
                }
                else
                {
                    string uiResponseStatusMsg = string.Empty;
                    Console.WriteLine(
                        $"Nothing to do, the list of cars to register is empty! Returning empty result with HTTP 200. " +
                        $"(RegistrationId = {registrationResult.RegistrationId})");

                    IEnumerable<IGrouping<string, CarRegistrationModel>> group = registerCarsModel.Cars.GroupBy(x => x.RegistrationId);

                    foreach (IGrouping<string, CarRegistrationModel> grp in group)
                    {
                        IList<CarRegistrationModel> dbApiCars = await _carManagementService.CarLeasingRepository.GetApiRegisteredCarsAsync(grp.Key);

                        foreach (CarRegistrationModel dbApiCar in dbApiCars)
                        {
                            CarRegistrationDto dbCar = new CarRegistrationDto
                            {
                                RegistrationId = dbApiCar.RegistrationId
                            };

                            bool hasMissingData = _carManagementService.HasMissingData(dbApiCar);
                            if (registerCarsModel.DeactivateAutoRegistrationProcessing && !hasMissingData)
                            {
                                Console.WriteLine(
                                    $"Automatic registration is deactivated (value = {registerCarsModel.DeactivateAutoRegistrationProcessing})" +
                                    $"and contains all relevant data (HasMissingData = {hasMissingData}). " +
                                    $"Set the transaction status of car {dbApiCar.VehicleIdentificationNumber} to {TransactionResult.ActionRequired.ToString()}" +
                                    $"Car (serialized as JSON): {dbCar}");

                                dbCar.TransactionState = (int?)TransactionResult.ActionRequired;
                                uiResponseStatusMsg = ApiResult.WARNING.ToString();
                            }
                            else
                            {
                                Console.WriteLine(
                                    $"Automatic registration is activated (value = {registerCarsModel.DeactivateAutoRegistrationProcessing}) " +
                                    $"or car doesn't contain all relevant data (HasMissingData = {hasMissingData}) or both. " +
                                    $"Set the transaction status of car {dbApiCar.VehicleIdentificationNumber} to {TransactionResult.MissingData.ToString()}. " +
                                    $"Car (serialized as JSON): {dbCar}");

                                dbCar.TransactionState = (int?)TransactionResult.MissingData;
                                uiResponseStatusMsg = TransactionResult.MissingData.ToString();
                            }

                            await _carRegistrationRepository.UpdateRegisteredCarAsync(dbCar, identity);
                        }
                    }

                    serviceResult.RegistrationId = registrationId;
                    serviceResult.Message = uiResponseStatusMsg;
                }

                return serviceResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving registration for {CarBrand.Toyota.ToString()} application: {ex}");
                throw ex;
            }
        }

        private async Task<ServiceResult> ForceBulkRegistration(IList<CarRegistrationModel> forceItems, string identity)
        {
            List<RegisterCarRequest> subsequentRegistrationRequestModels = new List<RegisterCarRequest>();
            RegistrationApiResponse subsequentRegistrationResponse = new RegistrationApiResponse();
            ServiceResult forceResponse = new ServiceResult();
            Dictionary<int, DateTime?> latestHistoryRowCreationDate = new Dictionary<int, DateTime?>();

            try
            {
                Console.WriteLine(
                            $"The registration with registration ids {string.Join(", ", forceItems.Select(x => x.RegistrationId))} has already been processed but forceRegisterment is true, " +
                            $"so the registration registration items will be registrationed again.");

                IList<CarRegistrationModel> currentDbCars = await _carRegistrationRepository.GetApiRegisteredCarsAsync(forceItems.Select(x => x.VehicleIdentificationNumber).ToList());

                foreach (CarRegistrationModel forceRegisterCar in forceItems)
                {
                    CarRegistrationModel currentDbCar = currentDbCars
                                            .Where(y => y.VehicleIdentificationNumber == forceRegisterCar.VehicleIdentificationNumber)
                                            .FirstOrDefault();

                    latestHistoryRowCreationDate.Add(
                        currentDbCar.RegisteredCarId,
                        (await _carRegistrationRepository.GetLatestCarHistoryEntryAsync(forceRegisterCar.VehicleIdentificationNumber)).RowCreationDate
                     );

                    _carManagementService.AssignCarValuesForUpdate(currentDbCar, forceRegisterCar, identity, source: "Force Registerment");

                    // Map the car to the needed request model for a subsequent registration transaction.
                    RegisterCarRequest item = new RegisterCarRequest()
                    {
                        RegistrationNumber = currentDbCar.RegistrationId,
                        Car = forceRegisterCar.VehicleIdentificationNumber,
                        ErpRegistrationNumber = string.Empty,
                        CompanyId = forceRegisterCar.CompanyId,
                        CustomerId = forceRegisterCar.CustomerId,
                    };
                    subsequentRegistrationRequestModels.Add(item);
                }

                forceResponse.TransactionId = subsequentRegistrationResponse.ActionResult.FirstOrDefault().TransactionId;
                if (subsequentRegistrationResponse.Status != ApiResult.ERROR.ToString())
                {
                    forceResponse.Message = subsequentRegistrationResponse.Status;
                }
                else
                {
                    // Revert all force cars to data status of latest history item.
                    IEnumerable<ServiceResult> failedTransactions = subsequentRegistrationResponse.ActionResult.Where(x => x.Message == ApiResult.ERROR.ToString());
                    foreach (ServiceResult item in failedTransactions)
                    {
                        await _carManagementService.HandleDataRevertAsync(item.RegisteredCarIds, identity);
                    }

                    throw new ForceRegistermentException("Subsequent registration transaction returned an error");
                }

                Console.WriteLine(
                    $"Forcing registration of an existing registration has been procecces. Return data (serialized as JSON): {JsonConvert.SerializeObject(forceResponse)}");
            }
            catch (ForceRegistermentException feEx)
            {
                Console.WriteLine(
                    $"Forced registration of an already existing registration registration failed. Values have been restored from car history." +
                    $"Data of forced registration (serialized as JSON): {JsonConvert.SerializeObject(forceItems)}: {feEx}");

                forceResponse.Message = "FORCE_ERROR";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Forced registration of an already existing registration failed due to unexpected reason." +
                    $"Data of forced registration (serialized as JSON): {JsonConvert.SerializeObject(forceItems)}: {ex}");

                forceResponse.Message = "FORCE_ERROR";
            }

            return forceResponse;
        }
    }    
}
