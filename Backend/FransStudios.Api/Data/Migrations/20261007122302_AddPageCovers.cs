using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FransStudios.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageCovers",
                columns: table => new
                {
                    Page = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Image_Key = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Image_Width = table.Column<int>(type: "int", nullable: false),
                    Image_Height = table.Column<int>(type: "int", nullable: false),
                    Image_Widths = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageCovers", x => x.Page);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageCovers");
        }
    }
}
