using AutoMapper;

namespace SlickDirectory;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<StateObj, StateObj>(); //cloning
        CreateMap<StateObj, TempDirectoryInstance>()
            .ConvertUsing<StateObjToTempDirectoryInstanceConverter>();
        CreateMap<TempDirectoryInstance, StateObj>()
            .ConvertUsing<TempDirectoryInstanceToStateObjConverter>();
    }

    public class StateObjToTempDirectoryInstanceConverter : ITypeConverter<StateObj, TempDirectoryInstance>
    {
        public TempDirectoryInstance Convert(StateObj source, TempDirectoryInstance destination, ResolutionContext context)
        {
            if (string.IsNullOrWhiteSpace(source.TempDirectory))
                throw new ArgumentException("TempDirectory is required");

            return new TempDirectoryInstance(source.TempDirectory);
        }
    }

    public class TempDirectoryInstanceToStateObjConverter : ITypeConverter<TempDirectoryInstance, StateObj>
    {
        public StateObj Convert(TempDirectoryInstance source, StateObj destination, ResolutionContext context)
        {
            if (source == null)
                throw new ArgumentException("TempDirectoryInstance is required");

            if (source.Path == null)
                throw new ArgumentException("TempDirectoryInstance.Path is required");

            return new StateObj { TempDirectory = source.Path.FullName };
        }
    }
}