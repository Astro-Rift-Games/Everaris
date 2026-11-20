'use strict';

const { MongoMemoryServer } = require('mongodb-memory-server');
const mongoose = require('mongoose');

async function run() {
  console.log('[DevMem] Iniciando servidor de MongoDB en memoria...');
  const mongod = await MongoMemoryServer.create();
  const uri = mongod.getUri();
  console.log('[DevMem] MongoDB en memoria listo en:', uri);

  process.env.NODE_ENV = process.env.NODE_ENV || 'development';
  process.env.PORT = process.env.PORT || '3000';
  process.env.MONGODB_URI = uri;
  process.env.JWT_SECRET = process.env.JWT_SECRET || 'dev-jwt-secret-not-for-deployment-grimhold-development-32chars';
  process.env.WEBHOOK_SECRET = process.env.WEBHOOK_SECRET || 'dev-webhook-secret-do-not-use-in-prod';
  process.env.ALLOW_DEV_WEBHOOK_SECRET = 'true';
  process.env.ENABLE_DEV_DEBUG_ROUTES = 'true';

  const config = require('../src/config/env');
  const { connectToDatabase } = require('../src/db/connection');
  const app = require('../src/app');
  const { checkAuthoritativeResultIndexes } = require('../src/db/authoritativeResultIndexes');

  await connectToDatabase(config.mongodbUri);
  await checkAuthoritativeResultIndexes();

  // Poblar cuenta semilla para pruebas locales en Unity
  const Account = require('../src/models/Account');
  const Character = require('../src/models/Character');
  const bcrypt = require('bcryptjs');

  const passwordHash = await bcrypt.hash('test1234', 10);
  const account = await Account.findOneAndUpdate(
    { username: 'tester_a' },
    { $set: { passwordHash } },
    { upsert: true, returnDocument: 'after' }
  );

  await Character.findOneAndUpdate(
    { accountId: account._id },
    {
      $setOnInsert: {
        name: 'Alpha',
        gold: 1000,
        'inventory.inventorySchemaVersion': 2
      }
    },
    { upsert: true, returnDocument: 'after' }
  );

  console.log('[DevMem] Cuenta de prueba lista:');
  console.log('         Usuario: tester_a');
  console.log('         Password: test1234');
  console.log('         Oro inicial: 1000');

  const server = app.listen(config.port, '0.0.0.0', () => {
    console.log(`[DevMem] Servidor backend escuchando en http://localhost:${config.port}`);
  });

  const cleanup = async () => {
    console.log('[DevMem] Apagando servidor...');
    server.close();
    await mongoose.connection.close();
    await mongod.stop();
    process.exit(0);
  };

  process.on('SIGINT', cleanup);
  process.on('SIGTERM', cleanup);
}

run().catch((err) => {
  console.error('[DevMem] Error al arrancar el servidor de desarrollo:', err);
  process.exit(1);
});
