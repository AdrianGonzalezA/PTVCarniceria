namespace Carnicerias.Api.Customers;

public static class FiscalCuit
{
    public static bool IsValid(string? value)
    {
        if (value is not { Length: 11 } || !value.All(char.IsAsciiDigit)) return false;
        int[] weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var sum = weights.Select((weight, index) => weight * (value[index] - '0')).Sum();
        var check = 11 - sum % 11;
        return (check == 11 ? 0 : check == 10 ? 9 : check) == value[10] - '0';
    }
}
