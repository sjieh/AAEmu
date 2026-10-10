-- Remove unused columns.
ALTER TABLE `auction_house`
DROP COLUMN `object_id`,
DROP COLUMN `grade`,
DROP COLUMN `flags`,
DROP COLUMN `detail_type`,
DROP COLUMN `creation_time`,
DROP COLUMN `lifespan_mins`,
DROP COLUMN `type_1`,
DROP COLUMN `unsecure_date_time`,
DROP COLUMN `unpack_date_time`,
DROP COLUMN `world_id_2`;

-- Change the id column type to bigint.
ALTER TABLE `auction_house`
MODIFY COLUMN `id` bigint NOT NULL AUTO_INCREMENT;

-- Change the item_id column type to bigint.
ALTER TABLE `auction_house`
MODIFY COLUMN `item_id` bigint NOT NULL;

-- Change the bid_world_id column type to int.
ALTER TABLE `auction_house`
MODIFY COLUMN `bid_world_id` int NOT NULL;

-- Change the character set and collation of the client_name and bidder_name columns.
ALTER TABLE `auction_house`
MODIFY COLUMN `client_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
MODIFY COLUMN `bidder_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL;

-- Update the table comment and options.
ALTER TABLE `auction_house`
COMMENT = 'Listed AH Items',
ROW_FORMAT = DYNAMIC;

-- Set the initial AUTO_INCREMENT value.
ALTER TABLE `auction_house`
AUTO_INCREMENT = 1;
