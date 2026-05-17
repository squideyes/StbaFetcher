namespace DatabentoDbnDownloader.Extenders;

public static class GenericValueExtenders
{
    public static R Convert<T, R>(this T value, Func<T, R> convert) =>
        convert(value);
}
