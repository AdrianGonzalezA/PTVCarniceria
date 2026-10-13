using Carnicerias.Bootstrap;

return args.FirstOrDefault() switch
{
    "create-terminal" or "list-branches" => await TerminalProvisionCommand.RunAsync(args),
    "seed-pos-visual" => await VisualPosSeedCommand.RunAsync(args),
    "reset-visual-passwords" => await VisualPasswordResetCommand.RunAsync(args),
    "create-superadmin" => await ManufacturerBootstrapCommand.RunAsync(args),
    _ => await UnsupportedCommandAsync()
};

static Task<int> UnsupportedCommandAsync()
{
    Console.Error.WriteLine("Comando no disponible. Creá el primer superadmin con create-superadmin; las empresas se crean desde su interfaz.");
    return Task.FromResult(2);
}
