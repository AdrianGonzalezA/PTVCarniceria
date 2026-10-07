using Carnicerias.Bootstrap;

return args.FirstOrDefault() == "create-terminal"
    ? await TerminalProvisionCommand.RunAsync(args)
    : await BootstrapCommand.RunAsync(args);
