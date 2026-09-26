using System;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

[DbContext(typeof(SagaDbContext))]
partial class SagaDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Domain.Sagas.SagaHistory", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint");

            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

            b.Property<string>("Details")
                .HasColumnType("text");

            b.Property<string>("FromState")
                .IsRequired()
                .HasMaxLength(40)
                .HasColumnType("character varying(40)");

            b.Property<DateTime>("OccurredAtUtc")
                .HasColumnType("timestamp with time zone");

            b.Property<Guid>("SagaInstanceId")
                .HasColumnType("uuid");

            b.Property<string>("ToState")
                .IsRequired()
                .HasMaxLength(40)
                .HasColumnType("character varying(40)");

            b.Property<string>("Trigger")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("character varying(100)");

            b.HasKey("Id");

            b.HasIndex("SagaInstanceId", "OccurredAtUtc");

            b.ToTable("saga_history", (string)null);
        });

        modelBuilder.Entity("Domain.Sagas.SagaInstance", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedNever()
                .HasColumnType("uuid");

            b.Property<string>("CorrelationId")
                .IsRequired()
                .HasMaxLength(120)
                .HasColumnType("character varying(120)");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("CurrentState")
                .IsRequired()
                .HasMaxLength(40)
                .HasColumnType("character varying(40)");

            b.Property<DateTime>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");

            b.HasIndex("CorrelationId")
                .IsUnique();

            b.ToTable("saga_instances", (string)null);
        });

        modelBuilder.Entity("Domain.Sagas.SagaHistory", b =>
        {
            b.HasOne("Domain.Sagas.SagaInstance", "SagaInstance")
                .WithMany("History")
                .HasForeignKey("SagaInstanceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("SagaInstance");
        });

        modelBuilder.Entity("Domain.Sagas.SagaInstance", b =>
        {
            b.Navigation("History");
        });
#pragma warning restore 612, 618
    }
}
