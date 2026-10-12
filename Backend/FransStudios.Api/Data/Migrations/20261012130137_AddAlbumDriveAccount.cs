using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FransStudios.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlbumDriveAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriveAccountEmail",
                table: "Albums",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovedDriveFileId",
                table: "AlbumFiles",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlbumMoves",
                columns: table => new
                {
                    AlbumId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetAccountEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    TargetFolderId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    Done = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumMoves", x => x.AlbumId);
                    table.ForeignKey(
                        name: "FK_AlbumMoves_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing album folders are in the Drive of the account that was connected when the album was made
            // (or, for albums from before the first connection was recorded, the first account connected).
            migrationBuilder.Sql("""
                UPDATE a SET DriveAccountEmail = COALESCE(
                    (SELECT TOP 1 c.AccountEmail FROM GoogleDriveConnections c WHERE c.ConnectedAt <= a.CreatedAt ORDER BY c.ConnectedAt DESC),
                    (SELECT TOP 1 c.AccountEmail FROM GoogleDriveConnections c ORDER BY c.ConnectedAt))
                FROM Albums a
                WHERE a.DriveFolderId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumMoves");

            migrationBuilder.DropColumn(
                name: "DriveAccountEmail",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "MovedDriveFileId",
                table: "AlbumFiles");
        }
    }
}
