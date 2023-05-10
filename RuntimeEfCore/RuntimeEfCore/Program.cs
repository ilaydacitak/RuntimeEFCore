using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics.CodeAnalysis;
using LinqToDB;
using DbContext = Microsoft.EntityFrameworkCore.DbContext;
using System.Data.Entity.Core.Metadata.Edm;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Data.Entity;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RuntimeEfCore

{
    class Program

    {
        //DbContext sınıfı, Entity Framework Core tarafından sağlanan bir sınıftır ve veritabanı işlemleri için bir temel sınıf sağlar.
        public class TargetDynamicDbContext : DbContext
        {
            // OnConfiguring ef core-DbContext tarafından sağlanan bir sınıftır. 
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                //ilgili adresle bağlama yapar
                optionsBuilder.UseSqlServer("data source=ILAYDCTK;initial catalog=CPEOPLE;integrated security=True; TrustServerCertificate=True"); 
            }
        }

        public static void Main(string[] args)
        {   
            // connectionString kontrolu
            var connectionString = args.Length > 0
                ? args[0]
                : throw new Exception("Pass connection string as a first parameter");

            // CreateMssqlScaffolder metotunu çağırarak veritabanı için Scaffold oluşturucusunu oluşturur.
            var scaffolder = CreateMssqlScaffolder();

            //Scaffold yapılandırması için gerekli kodlar
            var dbOpts = new DatabaseModelFactoryOptions();  //veritabanı modeli için kullanılacak seçeneklerin ayarlanması 
            var modelOpts = new ModelReverseEngineerOptions();  //veritabanından model oluşturulurken kullanılacak seçeneklerin ayarlanması
            var codeGenOpts = new ModelCodeGenerationOptions() // kod üretimi için seçeneklerin oluşturulması
                                                                //Scaffold oluşturucunun nasıl çalışacağını belirler ve
                                                                //Scaffold oluşturucunun DbContext ve varlık sınıflarını
                                                                //nasıl oluşturacağını yönlendirir. 
            {
                RootNamespace = "TypedDataContext", //Oluşturulacak kod dosyalarının kök ad alanı
                ContextName = "DataContext", //DbContext sınıfının adı.
                ContextNamespace = "TypedDataContext.Context", //DbContext sınıfının ad alanı.
                ModelNamespace = "TypedDataContext.Models", // varlık sınıflarının ad alanı.
                SuppressConnectionStringWarning = true //bağlantı dizesi belirtilmediği durumda verdiği uyarıyı engellemek için                
            };
            //veritabanı şemasını analiz edilmesi ve bu şemaya dayalı bir DbContext ve varlık sınıfları oluşturması
            var scaffoldedModelSources = scaffolder.ScaffoldModel(connectionString, dbOpts, modelOpts, codeGenOpts);

            //scaffoldedModelSources.ContextFile = ScaffoldedFile nesnesini temsilen kullanılmış 
            //oluşturulan kod dosyalarının yollarını bir liste olarak alır. 
            //Context kodunu içeren bir dize olarak döndürür.
            var sourceFiles = new List<string> { scaffoldedModelSources.ContextFile.Code };

            //AdditionalFiles koleksiyonundaki her ScaffoldedFile nesnesinin kodunu içeren dosyaların yollarını
            //bir liste olarak alır ve oluşturduğumuz listeye ekler
            //bu ek dosyalar Scaffold oluşturucunun herhangi bir özel seçeneği için eklenmiş kodlar olabilir
            sourceFiles.AddRange(scaffoldedModelSources.AdditionalFiles.Select(f => f.Code));

            //Scaffold oluşturucunun çıktı kodunu geçici olarak depolamak için bellekte yer ayırması
            using var peStream = new MemoryStream();

            //diğer varlıkların yüklenmesini otomatik olarak sağlayan özellik. core 5 den sonra kaldırılıp default olarak false verilmiş
            var enableLazyLoading = false;

            //derlenir ve peStream adlı bir MemoryStream nesnesine yazılır. EmitResult nesnesi, derleme işleminin sonucunu içerir.
            var result = GenerateCode(sourceFiles, enableLazyLoading).Emit(peStream);

            if (!result.Success)
            {
                var failures = result.Diagnostics
                    .Where(diagnostic => diagnostic.IsWarningAsError ||
                                         diagnostic.Severity == DiagnosticSeverity.Error);

                var error = failures.FirstOrDefault();
                throw new Exception($"{error?.Id}: {error?.GetMessage()}");
            }

            //DbContext adında AssemblyLoadContext nesnesi oluşturur. 
            //enableLazyLoading = false = nesne toplanmaz sürücüde kalır
            //enableLazyLoading = ture = toplanabilir yani çöp toplama sırasında bellekten kaldırılır
            var assemblyLoadContext = new AssemblyLoadContext("DbContext", isCollectible: !enableLazyLoading);

            //seek metotu = okuma konumunun ayarlama
            peStream.Seek(0, SeekOrigin.Begin);
            var assembly = assemblyLoadContext.LoadFromStream(peStream); // AssemblyLoadContext derleme

            var type = assembly.GetType("TypedDataContext.Context.DataContext");
            _ = type ?? throw new Exception("DataContext type not found");

            var constr = type.GetConstructor(Type.EmptyTypes);
            _ = constr ?? throw new Exception("DataContext ctor not found");

            DbContext dynamicContext = (DbContext)constr.Invoke(null); //DbContexten örenk 
            //örneğin model özelliğinden tüm varlık türlerini(tablo vs) çek - entityTypes lisete olarak sakla
            var entityTypes = dynamicContext.Model.GetEntityTypes();


            var targetDbContext = new TargetDynamicDbContext(); // hedef db örneği oluşturuldu


        //DENEMELER
           /* var views = entityTypes
                .Where(x => x.GetProperties().All(p => !p.IsPrimaryKey()))
                .ToList();

            // Tüm tetikleyicileri alın
            var triggers = entityTypes
                .Where(x => x.GetProperties().All(p => !p.IsPrimaryKey() && !p.IsForeignKey()))
                .ToList();

            /* Tüm saklı prosedürleri alın
            var storedProcedures = entityTypes
                .Where(x => x.GetProperties().All(p => p.IsParameter()))
                .ToList();

            foreach (var view in views)
            {
                Console.WriteLine(view);
                var tableName = view.GetTableName();

                // Sütunları belirleyin
                var columns = string.Join(", ", view.GetProperties().Select(p => $"[{p.GetColumnName()}]"));

                var sql = $"CREATE VIEW [{tableName}] ({columns}) AS SELECT {columns} FROM [SourceDatabase].[dbo].[{tableName}]";

                targetDbContext.Database.ExecuteSqlRaw(sql);
            }
            */

            var connectionString1 = dynamicContext.Database.GetDbConnection().ConnectionString; //hedef conn str
            var connectionString2 = targetDbContext.Database.GetDbConnection().ConnectionString; //ana conn str
            dynamicContext.Database.GetDbConnection().ConnectionString= connectionString2; //conn str değiştirildi

            dynamicContext.Database.EnsureCreated(); // hefed veritabanı oluşturulur
            dynamicContext.Database.Migrate(); //hedef veritabanının şeması "dynamicContext" olarak güncellebır


            dynamicContext.Database.GetDbConnection().ConnectionString = connectionString1; // geri aldım



            foreach (var entityType in dynamicContext.Model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                var items = (IQueryable<object>)dynamicContext.Query(entityType.Name);


                Console.WriteLine($"Table name: {tableName}, Entity type: {entityType.Name} contains {items.Count()} items");


                foreach (var item in items)
                {
                    var properties = item.GetType().GetProperties();
                    foreach (var property in properties)
                    {
                        
                        Console.WriteLine($"{property.Name}: {property.GetValue(item)}");
                        
                    }
                }

                


            }

            Console.ReadKey();

            if (!enableLazyLoading)
            {
                assemblyLoadContext.Unload();
            }
        }


            //Scaffold oluşturucuyu (Reverse Engineering) oluşturmak için kullanılan bir metottur. 
            //veritabanından varlık sınıflarının oluşturulmasını mümkün kılan ayarlar yappılır.
            [SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "We need it")]
            static IReverseEngineerScaffolder CreateMssqlScaffolder() =>
                new ServiceCollection()
                   .AddEntityFrameworkSqlServer() //SQL Server veritabanı sağlayıcısını kaydeder
                   .AddLogging() //Kayıt kaydeder
                   .AddEntityFrameworkDesignTimeServices() //EF Core tasarım zamanı hizmetlerini kaydeder
                    //Yalnıca bir örneğin oluşmasını sağlar
                   .AddSingleton<LoggingDefinitions, SqlServerLoggingDefinitions>() //Scaffold oluşturucusunun kayıt hizmetleri
                   .AddSingleton<IRelationalTypeMappingSource, SqlServerTypeMappingSource>() //ilişkisel veri türü eşlemesini sağlar.
                   .AddSingleton<IAnnotationCodeGenerator, AnnotationCodeGenerator>() //EF Core varlıkları için kod oluşturur.
                   .AddSingleton<IDatabaseModelFactory, SqlServerDatabaseModelFactory>() // veritabanı modeli oluşturucularını sağlar
                   .AddSingleton<IProviderConfigurationCodeGenerator, SqlServerCodeGenerator>() //EF Core sağlayıcı yapılandırması
                   .AddSingleton<IScaffoldingModelFactory, RelationalScaffoldingModelFactory>() //Scaffold modeli oluşturucularını sağlar
                   .AddSingleton<IPluralizer, Bricelam.EntityFrameworkCore.Design.Pluralizer>() //EF Core çoğullama özelliğini sağlar
                                                                                                //bir veritabanındaki "Person" adlı bir tabloyu
                                                                                                //modellemek istediğinizde, Scaffold oluşturucusu
                                                                                                //"People" adlı bir varlık sınıfı oluşturacaktır.
                   .AddSingleton<ProviderCodeGeneratorDependencies>() //gerekli bağımlılıkları sağlar. -EF Core sağlayıcı-
                   .AddSingleton<AnnotationCodeGeneratorDependencies>() // erekli bağımlılıkları sağlar. -EF Core varlık-
                   .BuildServiceProvider() //ServiceProvider=bir bileşenin ihtiyaç duyduğu diğer bileşenleri otomatik olarak oluşturan yapıdır.
                   .GetRequiredService<IReverseEngineerScaffolder>();



            //derleme işlemi sırasında kullanılacak referansların oluşturulduğu bölümdür
            static List<MetadataReference> CompilationReferences(bool enableLazyLoading)
            {
                var refs = new List<MetadataReference>(); // referansları listeler
                var referencedAssemblies = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
                refs.AddRange(referencedAssemblies.Select(a => MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

                
                // referanslar belirtilen türlerin ait olduğu DLL dosyalarının yerlerinden elde edilir
                refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(BackingFieldAttribute).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location));

                if (enableLazyLoading)
                {
                    refs.Add(MetadataReference.CreateFromFile(typeof(ProxiesExtensions).Assembly.Location)); // ekledi
                }

                return refs;
            }

            //Derlenen kodun çıktı türü = dll
            private static CSharpCompilation GenerateCode(List<string> sourceFiles, bool enableLazyLoading)
            {
                var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);

                var parsedSyntaxTrees = sourceFiles.Select(f => SyntaxFactory.ParseSyntaxTree(f, options));

                return CSharpCompilation.Create($"DataContext.dll",
                    parsedSyntaxTrees,
                    references: CompilationReferences(enableLazyLoading),
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release,
                        assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));
            }
        }
    }

        public static class DynamicContextExtensions
        {
            public static IQueryable Query(this DbContext context, string entityName) =>
                context.Query(entityName, context.Model.FindEntityType(entityName).ClrType); // sorgu döndr

            static readonly MethodInfo SetMethod =
                typeof(DbContext).GetMethod(nameof(DbContext.Set), 1, new[] { typeof(string) }) ??
                throw new Exception($"Type not found: DbContext.Set");

            public static IQueryable Query(this DbContext context, string entityName, Type entityType) =>
                (IQueryable)SetMethod.MakeGenericMethod(entityType)?.Invoke(context, new[] { entityName }) ??
                throw new Exception($"Type not found: {entityType.FullName}");


        }
    
