SELECT current_database(), count(*),
       count(*) FILTER (WHERE "MigrationId" = '20261104133006_MakeProductCostOptional')
FROM public."__EFMigrationsHistory";
