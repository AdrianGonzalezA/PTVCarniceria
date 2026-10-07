module.exports = {
  packagerConfig: {
    asar: true,
    executableName: 'Carnicerias.Pos',
    ignore: [
      /^\/(?:scripts|src|tests)(?:\/|$)/,
      /^\/tsconfig(?:\.build)?\.json$/,
    ],
    name: 'Carnicerias POS',
  },
  makers: [],
};
