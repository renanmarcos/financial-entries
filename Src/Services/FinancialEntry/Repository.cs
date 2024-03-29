using System;
using System.Collections.Generic;
using System.Linq;
using FinancialEntries.Services.Cache;
using FinancialEntries.Services.Firestore;
using Google.Cloud.Firestore;
using FinancialEntryModel = FinancialEntries.Models.FinancialEntry;
using StoreModel = FinancialEntries.Models.Store;

namespace FinancialEntries.Services.FinancialEntry
{
    public class Repository : IRepository<FinancialEntryModel>
    {
        private readonly IDatabase _database;

        private readonly ISubjectCache _subject;

        private const string Collection = "FinancialEntries";

        

        public Repository(IDatabase database)
        {
            _database = database;
            _subject = StaticServiceProvider
                        .Provider.GetService(typeof(ISubjectCache)) as ISubjectCache;
        }

        public bool Delete(string id)
        {
            var task = _database.GetInstance()
                .Collection(Collection)
                .Document(id)
                .DeleteAsync(Precondition.MustExist);

            try
            {
                task.Wait();
            } catch (Exception)
            {
                return false;
            }

            _subject.GetInstance().NotifyObservers();

            return true;
        }

        public FinancialEntryModel Update(FinancialEntryModel model)
        {
            var database = _database.GetInstance();
            var storeReference = database.Collection("Stores").Document(model.StoreId);
            model.StoreReference = storeReference;

            database.Collection(Collection)
                .Document(model.Id)
                .SetAsync(model, SetOptions.Overwrite);
            
            var store = storeReference.GetSnapshotAsync();
            store.Wait();
            model.Store = store.Result.ConvertTo<StoreModel>();

            _subject.GetInstance().NotifyObservers();

            return model;
        }

        public FinancialEntryModel Get(string id)
        {
            var snapshot = _database.GetInstance()
                .Collection(Collection)
                .Document(id)
                .GetSnapshotAsync();
            snapshot.Wait();

            if (!snapshot.Result.Exists)
            {
                return null;
            }

            var financialEntry = snapshot.Result.ConvertTo<FinancialEntryModel>();
            snapshot = financialEntry.StoreReference.GetSnapshotAsync();
            snapshot.Wait();
            financialEntry.Store = snapshot.Result.ConvertTo<StoreModel>();

            return financialEntry;
        }

        public IEnumerable<FinancialEntryModel> Index()
        {
            var snapshot = _database.GetInstance()
                .Collection(Collection)
                .GetSnapshotAsync();
            snapshot.Wait();

            return snapshot.Result.Select(document => 
            {
                var financialEntry = document.ConvertTo<FinancialEntryModel>();
                var documentSnapshot = financialEntry.StoreReference.GetSnapshotAsync();
                documentSnapshot.Wait();
                var result = documentSnapshot.Result;

                if (result.Exists) 
                {
                    financialEntry.Store = result.ConvertTo<StoreModel>();
                }

                return financialEntry;
            }).ToList();
        }

        public FinancialEntryModel Insert(FinancialEntryModel model)
        {
            var database = _database.GetInstance();
            var storeReference = database.Collection("Stores").Document(model.StoreId);
            model.StoreReference = storeReference;
            
            var task = database.Collection(Collection).AddAsync(model);
            task.Wait();
            model.Id = task.Result.Id;

            var store = storeReference.GetSnapshotAsync();
            store.Wait();
            model.Store = store.Result.ConvertTo<StoreModel>();

            _subject.GetInstance().NotifyObservers();

            return model;
        }

        public RecuperableCause GetCannotStoreReason(FinancialEntryModel model)
        {
            var snapshot = _database.GetInstance()
                .Collection("Stores")
                .Document(model.StoreId)
                .GetSnapshotAsync();
            
            snapshot.Wait();

            if (!snapshot.Result.Exists)
                return new RecuperableCause("StoreId is not a valid reference");

            return null;
        }
    }
}