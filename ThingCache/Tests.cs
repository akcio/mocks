using FakeItEasy;
using FluentAssertions;
using FluentAssertions.Common;
using NUnit.Framework;

namespace MockFramework
{
    public class Tests
    {
        private IThingService service;
        private ThingCache cache;
        
        private const string thingKey1 = "T-Shirt";
        private Thing thing1 = new Thing(thingKey1);

        private const string thingKey2 = "Glasses";
        private Thing thing2 = new Thing(thingKey2);
        
        [SetUp]
        public void Configure()
        {
            service = A.Fake<IThingService>();
            cache = new ThingCache(service);
            A.CallTo(() => service.TryRead(A<string>.Ignored, out thing1)).Returns(false);
            A.CallTo(() => service.TryRead(thingKey1, out thing1))
                .Returns(true);
            A.CallTo(() => service.TryRead(thingKey2, out thing2))
                .Returns(true);
        }

        [Test]
        public void TestGetUnregisteredKey()
        {
            var a = cache.Get("test_tmp");
            Assert.That(a == null);
        }
        
        [Test]
        public void TestGet()
        {
            var a = cache.Get(thingKey1);
            Assert.That(a == thing1);
        }

        [Test]
        public void TestCanReadShouldCallOnlyOnce()
        {
            
            var firstItem = cache.Get(thingKey2);
            var secondItem = cache.Get(thingKey2);
            Assert.That(firstItem == secondItem);
            Assert.That(firstItem == thing2);
            A.CallTo(() => service.TryRead(thingKey2, out thing2)).MustHaveHappenedOnceExactly();
            A.CallTo(() => service.TryRead(A<string>.Ignored, out thing2)).MustHaveHappenedOnceExactly();
        }

        [Test]
        public void TestReadMultipleKeys()
        {
            var firstItem = cache.Get(thingKey1);
            var secondItem = cache.Get(thingKey2);
            Assert.That(firstItem == thing1);
            Assert.That(secondItem == thing2);
            Assert.That(firstItem != secondItem);
            A.CallTo(() => service.TryRead(thingKey1, out thing1)).MustHaveHappenedOnceExactly();
            A.CallTo(() => service.TryRead(thingKey2, out thing2)).MustHaveHappenedOnceExactly();
            A.CallTo(() => service.TryRead(A<string>.Ignored, out thing2)).MustHaveHappened(2, Times.Exactly);
        }

    }
}