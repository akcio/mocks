using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
using FakeItEasy;
using FileSender.Dependencies;
using FluentAssertions;
using NUnit.Framework;

namespace FileSender
{
    public class FileSender
    {
        private readonly ICryptographer cryptographer;
        private readonly ISender sender;
        private readonly IRecognizer recognizer;

        public FileSender(
            ICryptographer cryptographer,
            ISender sender,
            IRecognizer recognizer)
        {
            this.cryptographer = cryptographer;
            this.sender = sender;
            this.recognizer = recognizer;
        }

        public Result SendFiles(File[] files, X509Certificate certificate)
        {
            return new Result
            {
                SkippedFiles = files
                    .Where(file => !TrySendFile(file, certificate))
                    .ToArray()
            };
        }

        private bool TrySendFile(File file, X509Certificate certificate)
        {
            Document document;
            if (!recognizer.TryRecognize(file, out document))
                return false;
            if (!CheckFormat(document) || !CheckActual(document))
                return false;
            var signedContent = cryptographer.Sign(document.Content, certificate);
            return sender.TrySend(signedContent);
        }

        private bool CheckFormat(Document document)
        {
            return document.Format == "4.0" ||
                   document.Format == "3.1";
        }

        private bool CheckActual(Document document)
        {
            return document.Created.AddMonths(1) > DateTime.Now;
        }

        public class Result
        {
            public File[] SkippedFiles { get; set; }
        }
    }

    //TODO: реализовать недостающие тесты
    [TestFixture]
    public class FileSender_Should
    {
        private FileSender fileSender;
        private ICryptographer cryptographer;
        private ISender sender;
        private IRecognizer recognizer;
        private string fileName;
        private DateTime now;
        private Random random;
        private const string Format40 = "4.0";
        private const string Format31 = "3.1";

        private readonly X509Certificate certificate = new X509Certificate();
        
        [SetUp]
        public void SetUp()
        {
            fileName = "temp.file";
            now = DateTime.Now;
            random = new Random();
            
            cryptographer = A.Fake<ICryptographer>();
            sender = A.Fake<ISender>();
            recognizer = A.Fake<IRecognizer>();
            fileSender = new FileSender(cryptographer, sender, recognizer);
            
            A.CallTo(() => cryptographer.Sign(A<byte[]>._, certificate))
                .Returns(GetFileContent());
            
            A.CallTo(() => sender.TrySend(A<byte[]>._))
                .Returns(true);
        }

        private byte[] GetSigned(Document document)
        {
            var signedContent = GetFileContent();
            A.CallTo(() => cryptographer.Sign(document.Content, certificate))
                .Returns(signedContent);
            return signedContent;
        }

        private byte[] GetFileContent()
        {
            var fileSize = random.Next(20) + 1;
            var content = new byte[fileSize];
            random.NextBytes(content);

            return content;
        }
        
        private File GetFileRecognized(Document document)
        {
            var file = new File(document.Name, document.Content);

            A.CallTo(() => recognizer.TryRecognize(file, out document))
                .Returns(true);

            return file;
        }

        [TestCase(Format40)]
        [TestCase(Format31)]
        public void Send_WhenGoodFormat(string format)
        {
            var document = new Document(fileName, GetFileContent(), now, format);
            var file = GetFileRecognized(document);
            var signed = GetSigned(document);

            A.CallTo(() => recognizer.TryRecognize(file, out document))
                .Returns(true);
            
            A.CallTo(() => sender.TrySend(signed))
                .Returns(true);

            var sendingResult = fileSender.SendFiles(new[] {file}, certificate);
            sendingResult.SkippedFiles.Should().BeEmpty();
            A.CallTo(() => sender.TrySend(signed)).MustHaveHappened();
        }

        [Test]
        public void Skip_WhenBadFormat()
        {
            const string badFormat = "1.0";
            var document = new Document(fileName, GetFileContent(), now, badFormat);
            var file = GetFileRecognized(document);
            var sendingResult = fileSender.SendFiles(new[] { file }, certificate);

            sendingResult.SkippedFiles.Should().BeEquivalentTo(file);
            A.CallTo(() => sender.TrySend(A<byte[]>._)).MustNotHaveHappened();
            
        }

        [Test]
        public void Skip_WhenOlderThanAMonth()
        {
            var moreThanMonthAgo = now.AddMonths(-1).AddSeconds(-1);
            var document = new Document(fileName, GetFileContent(), moreThanMonthAgo, Format31);
            var file = GetFileRecognized(document);
            var sendingResult = fileSender.SendFiles(new[] { file }, certificate);

            sendingResult.SkippedFiles.Should().BeEquivalentTo(file);
            A.CallTo(() => sender.TrySend(A<byte[]>._)).MustNotHaveHappened();
        }

        [Test]
        public void Send_WhenYoungerThanAMonth()
        {
            var youngerThanMonthAgo = now.AddMonths(-1).AddSeconds(1);
            var document = new Document(fileName, GetFileContent(), youngerThanMonthAgo, Format31);
            var file = GetFileRecognized(document);
            var sendingResult = fileSender.SendFiles(new[] { file }, certificate);

            sendingResult.SkippedFiles.Should().BeEmpty();
            A.CallTo(() => sender.TrySend(A<byte[]>._)).MustHaveHappened();
        }

        [Test]
        public void Skip_WhenSendFails()
        {
            var document = new Document(fileName, GetFileContent(), now, Format31);
            var file = GetFileRecognized(document);
            
            A.CallTo(() => sender.TrySend(A<byte[]>._))
                .Returns(false);
            
            var sendingResult = fileSender.SendFiles(new[] { file }, certificate);

            sendingResult.SkippedFiles.Should().BeEquivalentTo(file);
        }

        [Test]
        public void Skip_WhenNotRecognized()
        {
            var file = new File(fileName, GetFileContent());
            
            var sendingResult = fileSender.SendFiles(new[] { file }, certificate);
            sendingResult.SkippedFiles.Should().BeEquivalentTo(file);
            A.CallTo(() => sender.TrySend(A<byte[]>._)).MustNotHaveHappened();
        }

        [Test]
        public void IndependentlySend_WhenSeveralFilesAndSomeAreInvalid()
        {
            var badFormat = "1.0";
            var invalidDocument1 = new Document(fileName, GetFileContent(), now, badFormat);
            var invalidDocument2 = new Document(fileName, GetFileContent(), now, badFormat);
            var documents = new []
            {
                invalidDocument1,
                new Document(fileName, GetFileContent(), now, Format40),
                invalidDocument2,
                new Document(fileName, GetFileContent(), now, Format31)
            };
            var recognized = documents.Select(GetFileRecognized).ToArray();
            var sendingResult = fileSender.SendFiles(recognized, certificate);
            A.CallTo(() => sender.TrySend(A<byte[]>._)).MustHaveHappenedTwiceExactly();
            sendingResult.SkippedFiles.Should().BeEquivalentTo(new [] {recognized[0], recognized[2]});
        }

        [Test]
        public void IndependentlySend_WhenSeveralFilesAndSomeCouldNotSend()
        {
            var documents = new []
            {
                new Document(fileName, GetFileContent(), now, Format40),
                new Document(fileName, GetFileContent(), now, Format31),
                new Document(fileName, GetFileContent(), now, Format40),
                new Document(fileName, GetFileContent(), now, Format31)
            };
            var recognized = documents.Select(GetFileRecognized).ToArray();

            A.CallTo(() => sender.TrySend(A<byte[]>._))
                .ReturnsNextFromSequence(false, true, true, false);

            var sendingResult = fileSender.SendFiles(recognized, certificate);
            sendingResult.SkippedFiles.Should().BeEquivalentTo(new [] {recognized[0], recognized[3]});
        }
    }
}
