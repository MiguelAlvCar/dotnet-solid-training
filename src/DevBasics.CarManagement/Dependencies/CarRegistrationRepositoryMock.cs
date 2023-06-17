using AutoMapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevBasics.CarManagement.Dependencies
{
    public interface ICarRegistrationRepository
    {
        Task<IList<CarRegistrationLogDto>> GetCarHistoryAsync(string carIdentificationNumber);
        Task<IList<CarRegistrationDto>> GetCarsAsync(IList<string> carIdentificationNumbers);
        Task<CarRegistrationModel> GetApiRegisteredCarAsync(int carDatasetId);
        Task<IList<CarRegistrationModel>> GetApiRegisteredCarsAsync(IList<string> carIdentificationNumbers);
        Task<IList<CarRegistrationModel>> GetApiRegisteredCarsAsync(string registrationRegistrationId);
        Task<CarRegistrationLogDto> GetLatestCarHistoryEntryAsync(string carIdentificationNumber);
        Task<int> UpdateRegisteredCarAsync(CarRegistrationDto dbCar, string identity, bool withHistory = true);
        Task UpdateErpRegistrationItemAsync(ErpRegistermentRegistration erpRegistermentRegistration);
    }

    internal sealed class CarRegistrationRepository : ICarRegistrationRepository
    {
        private readonly IUpdateCar _updateCar;
        private readonly IBulkRegistrationService _service;
        private readonly IMapper _mapper;

        public CarRegistrationRepository(
            IUpdateCar updateCar,
            IBulkRegistrationService service,
            IMapper mapper)
        {
            _updateCar = updateCar;
            _service = service;
            _mapper = mapper;
        }

        public Task<CarRegistrationModel> GetApiRegisteredCarAsync(int carDatasetId)
        {
            var repository = _updateCar as LeasingRegistrationRepository;
            if (repository.Registrations.ContainsKey(carDatasetId))
            {
                return Task.FromResult(MapToModel(repository.Registrations[carDatasetId].Item1));
            }

            return Task.FromResult<CarRegistrationModel>(null);
        }

        public Task<IList<CarRegistrationModel>> GetApiRegisteredCarsAsync(IList<string> carIdentificationNumbers)
        {
            var repository = _updateCar as LeasingRegistrationRepository;
            var models = repository
                .Registrations
                .Values
                .Select(rgstrtn => rgstrtn.Item1)
                .Where(rgstrtn => carIdentificationNumbers.Contains(rgstrtn.CarIdentificationNumber))
                .Select(MapToModel)
                .ToList();

            return Task.FromResult(models as IList<CarRegistrationModel>);
        }

        public Task<IList<CarRegistrationModel>> GetApiRegisteredCarsAsync(string registrationRegistrationId)
        {
            var repository = _updateCar as LeasingRegistrationRepository;
            var models = repository
                .Registrations
                .Values
                .Select(rgstrtn => rgstrtn.Item1)
                .Where(rgstrtn => rgstrtn.RegistrationId.Equals(registrationRegistrationId))
                .Select(MapToModel)
                .ToList();

            return Task.FromResult(models as IList<CarRegistrationModel>);
        }

        public Task<IList<CarRegistrationLogDto>> GetCarHistoryAsync(string carIdentificationNumber)
        {
            var service = _service as BulkRegistrationServiceMock;
            var result = new List<CarRegistrationLogDto>();
            foreach (var request in service.Requests)
            {
                foreach (var registration in request.Item2.Registrations)
                {
                    foreach (var delivery in registration.Deliveries)
                    {
                        foreach (var car in delivery.Cars)
                        {
                            if (!car.VehicleIdentificationNumber.Equals(carIdentificationNumber))
                            {
                                continue;
                            }

                            var dto = new CarRegistrationLogDto
                            {
                                RegistrationId = registration.RegistrationNumber,
                                RowCreationDate = request.Item1,
                                TransactionState = TransactionResult.Registered.ToString(),
                                TransactionType = RegistrationType.Register.ToString(),
                                UserName = "LOL"
                            };

                            result.Add(dto);
                        }
                    }
                }
            }

            return Task.FromResult<IList<CarRegistrationLogDto>>(result);
        }

        public Task<IList<CarRegistrationDto>> GetCarsAsync(IList<string> carIdentificationNumbers)
        {
            var repository = _updateCar as LeasingRegistrationRepository;
            var models = repository
                .Registrations
                .Values
                .Select(rgstrtn => rgstrtn.Item1)
                .Where(rgstrtn => carIdentificationNumbers.Contains(rgstrtn.CarIdentificationNumber))
                .ToList();

            return Task.FromResult(models as IList<CarRegistrationDto>);
        }

        public async Task<CarRegistrationLogDto> GetLatestCarHistoryEntryAsync(string carIdentificationNumber)
        {
            var result = (await GetCarHistoryAsync(carIdentificationNumber))
                .OrderByDescending(hstry => hstry.RowCreationDate)
                .FirstOrDefault();

            return result;
        }

        public Task UpdateErpRegistrationItemAsync(ErpRegistermentRegistration erpRegistermentRegistration)
        {
            return Task.CompletedTask;
        }

        public async Task<int> UpdateRegisteredCarAsync(CarRegistrationDto dbCar, string identity, bool withHistory = true)
        {
            return await _updateCar.UpdateCarAsync(dbCar) ? 1 : 0;
        }

        private CarRegistrationModel MapToModel(CarRegistrationDto dto)
        {
            return _mapper.Map<CarRegistrationModel>(dto);
        }
    }
}
