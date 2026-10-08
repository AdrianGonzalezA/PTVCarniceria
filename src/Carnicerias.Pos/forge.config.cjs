module.exports = {
  packagerConfig: {
    asar: { unpack: '**/*.node' },
    executableName: 'Carnicerias.Pos',
    ignore: [
      /^\/(?:scripts|src|tests)(?:\/|$)/,
      /^\/tsconfig(?:\.build)?\.json$/,
    ],
    name: 'Carnicerias POS',
  },
  // SerialPort ships a Node-API Windows binary; rebuilding it requires MSVC and is unnecessary here.
  rebuildConfig: { ignoreModules: ['@serialport/bindings-cpp'] },
  makers: [],
};
