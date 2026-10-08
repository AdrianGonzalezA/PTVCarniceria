using Carnicerias.Bootstrap;

return args.FirstOrDefault() switch
{
    "create-terminal" or "list-branches" => await TerminalProvisionCommand.RunAsync(args),
    "seed-pos-visual" => await VisualPosSeedCommand.RunAsync(args),
    "reset-visual-passwords" => await VisualPasswordResetCommand.RunAsync(args),
    _ => await BootstrapCommand.RunAsync(args)
};
