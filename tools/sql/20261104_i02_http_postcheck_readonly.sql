SELECT current_database(),
       (SELECT count(*) FROM platform_access.companies
        WHERE "Code" LIKE 'I02HTTP-%'),
       (SELECT count(*) FROM inventory.pieces
        WHERE "ExternalIdentifier" IN ('999002', '250661', '071000')
          AND "Status" = 5);
