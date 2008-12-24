using System;
using System.Collections.Generic;
using TradeLib;
using NUnit.Framework;

namespace TestTradeLib
{
    [TestFixture]
    public class TestBroker
    {
        public TestBroker()
        {

        }
        Broker broker = new Broker();
        int fills = 0, orders = 0;
        const string s = "TST";

        [Test]
        public void Basics()
        {
            Broker broker = new Broker();
            broker.GotFill += new FillDelegate(broker_GotFill);
            broker.GotOrder += new OrderDelegate(broker_GotOrder);
            Order o = new Order();
            uint failsoninvalid= broker.sendOrder(o);
            Assert.AreNotEqual((int)TL2.OK,failsoninvalid);
            Assert.That(orders == 0);
            Assert.That(fills == 0);
            o = new BuyMarket(s, 100);
            Assert.That(broker.sendOrder(o)>0);
            Assert.That(orders == 1);
            Assert.That(fills == 0);
            Assert.That(broker.Execute(Tick.NewTrade(s,10,200)) == 1);
            Assert.That(fills == 1);

            // test that a limit order is not filled outside the market
            o = new BuyLimit(s, 100, 9);
            broker.sendOrder(o);
            Assert.AreEqual(0, broker.Execute(Tick.NewTrade(s, 10, 100)));
            Assert.That(fills == 1); // redudant but for counting

            // test that limit order is filled inside the market
            Assert.AreEqual(1, broker.Execute(Tick.NewTrade(s, 8, 100)));
            Assert.That(fills == 2);

            Order x = new Order();
            // test that a market order is filled when opposite book exists
            o = new SellLimit(s, 100, 11);
            x = new BuyMarket(s, 100);
            const string t2 = "trader2";
            x.Account = t2;
            broker.sendOrder(o);
            broker.sendOrder(x);
            Assert.AreEqual(3, fills); 

            // test that a market order is not filled when no book exists
            // on opposite side

            // clear existing orders
            broker.CancelOrders();
            o = new SellMarket(s, 100);
            o.Account = t2;
            broker.sendOrder(o);
            Assert.AreEqual(3, fills);
            

            
        }


        void broker_GotOrder(Order o)
        {
            orders++;
        }

        void broker_GotFill(Trade t)
        {
            fills++;
        }

        int gottickDP = 0;
        Tick receivedtickDP;

        [Test]
        public void DataProvider()
        {
            Tick t = Tick.NewTrade(s, 10, 700);
            // feature to pass-through ticks to any subscriber
            // this can be connected to tradelink library to allow filtered subscribptions
            // and interapplication communication
            Broker broker = new Broker();
            broker.GotTick += new TickDelegate(broker_GotTick);
            Assert.That((receivedtickDP == null) && (gottickDP == 0));
            broker.Execute(t); // should fire a gotTick
            Assert.That(gottickDP != 0);
            Assert.That((receivedtickDP != null) && (receivedtickDP.trade == t.trade));

        }
        void broker_GotTick(Tick tick)
        {
            receivedtickDP = new Tick(tick);
            gottickDP++;
        }

        [Test]
        public void BBO()
        {
            Broker broker = new Broker();
            const string s = "TST";
            const decimal p1 = 10m;
            const decimal p2 = 11m;
            const int x = 100;
            Order bid,offer;

            // send bid, make sure it's BBO (since it's only order on any book)
            broker.sendOrder(new BuyLimit(s, x, p1));
            bid = broker.BestBid(s);
            offer = broker.BestOffer(s);
            Assert.That(bid.isValid && (bid.price==p1) && (bid.size==x), bid.ToString());
            Assert.That(!offer.isValid, offer.ToString());

            // add better bid, make sure it's BBO
            uint id1 = broker.sendOrder(new BuyLimit(s, x, p2));
            bid = broker.BestBid(s);
            offer = broker.BestOffer(s);
            Assert.That(bid.isValid && (bid.price == p2) && (bid.size == x), bid.ToString());
            Assert.That(!offer.isValid, offer.ToString());

            // add another bid at same price on another account, make sure it's additive
            uint id2 = broker.sendOrder(new BuyLimit(s, x, p2),new Account("ANOTHER_ACCOUNT"));
            bid = broker.BestBid(s);
            offer = broker.BestOffer(s);
            Assert.That(bid.isValid && (bid.price == p2) && (bid.size == (2*x)), bid.ToString());
            Assert.That(!offer.isValid, offer.ToString());

            // cancel order and make sure bbo returns
            broker.CancelOrder(id1);
            broker.CancelOrder(id2);
            bid = broker.BestBid(s);
            offer = broker.BestOffer(s);
            Assert.That(bid.isValid && (bid.price == p1) && (bid.size == x), bid.ToString());
            Assert.That(!offer.isValid, offer.ToString());

            // other test ideas
            // replicate above tests for sell-side


        }






        [Test]
        public void MultiAccount()
        {
            const string sym = "TST";

            const string me = "tester";
            const string other = "anotherguy";
            Account a = new Account(me);
            Account b = new Account(other);
            Account c = new Account("sleeper");
            Order oa = new BuyMarket(sym,100);
            Order ob = new BuyMarket(sym, 100);
            oa.time = Util.ToTLTime(DateTime.Now);
            oa.date = Util.ToTLDate(DateTime.Now);
            ob.time = Util.ToTLTime(DateTime.Now);
            ob.date = Util.ToTLDate(DateTime.Now);

            oa.Account = me;
            ob.Account = other;
            // send order to account for jfranta
            Assert.That(broker.sendOrder(oa)>0);
            Assert.That(broker.sendOrder(ob)>0);
            Tick t = new Tick(sym);
            t.trade = 100m;
            t.size = 200;
            t.date = Util.ToTLDate(DateTime.Now);
            t.time = Util.ToTLTime(DateTime.Now);
            Assert.AreEqual(2,broker.Execute(t));
            Position apos = broker.GetOpenPosition(sym,a);
            Position bpos = broker.GetOpenPosition(sym,b);
            Position cpos = broker.GetOpenPosition(sym, c);
            Assert.That(apos.isLong);
            Assert.AreEqual(100,apos.Size);
            Assert.That(bpos.isLong);
            Assert.AreEqual(100,bpos.Size);
            Assert.That(cpos.isFlat);
            // make sure that default account doesn't register
            // any trades
            Assert.That(broker.GetOpenPosition(sym).isFlat);
        }

        [Test]
        public void OPGs()
        {
            Broker broker = new Broker();
            const string s = "TST";
            // build and send an OPG order
            Order opg = new BuyOPG(s, 200, 10);
            broker.sendOrder(opg);

            // build a tick on another exchange
            Tick it = Tick.NewTrade(s, 9, 100);
            it.ex = "ISLD";

            // fill order (should fail)
            int c = broker.Execute(it);
            Assert.AreEqual(0, c);

            // build opening price for desired exchange
            Tick nt = Tick.NewTrade(s, 9, 10000);
            nt.ex = "NYS";
            // fill order (should work)

            c = broker.Execute(nt);

            Assert.AreEqual(1, c);

            // add another OPG, make sure it's not filled with another tick

            Tick next = Tick.NewTrade(s, 9, 2000);
            next.ex = "NYS";

            Order late = new BuyOPG(s, 200, 10);
            broker.sendOrder(late);
            c = broker.Execute(next);
            Assert.AreEqual(0, c);

        }
    }
}
