﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Devices.Radios.nRF24L01;
using Windows.Devices.Spi;

namespace X10SerialSlave.Server
{
    public enum Roles
    {
        RolePingOut = 1,
        RolePongBack
    }

    public sealed class Rf24Server : IX10Controller
    {
        private Radio _radio;
        private long[] _pipes = new[] { 0xF0F0F0F0E1L, 0xF0F0F0F0D2L };
        private string[] _roleFrendlyNames = { "invalid", "Ping out", "Pong back" };
        private Roles _role = Roles.RolePingOut;

        public byte[] GetBytes()
        {
            //return _rf.ReceivePayload();
            return null;
        }

        public async void Initialize()
        {
            GpioPin cePin = GpioController.GetDefault().OpenPin(26);

            DeviceInformationCollection devicesInfo = await DeviceInformation.FindAllAsync(SpiDevice.GetDeviceSelector("SPI0"));
            SpiDevice spiDevice = await SpiDevice.FromIdAsync(devicesInfo[0].Id, new SpiConnectionSettings(0)
            {
                ClockFrequency = 1000000,
                Mode = SpiMode.Mode0
            });

            _radio = new Radio(cePin, spiDevice);
            _radio.Begin();
            _radio.SetRetries(15, 15);

            if (_role == Roles.RolePingOut)
            {
                _radio.OpenWritingPipe(_pipes[0]);
                _radio.OpenReadingPipe(1, _pipes[1]);
            }
            else
            {
                _radio.OpenWritingPipe(_pipes[1]);
                _radio.OpenReadingPipe(1, _pipes[0]);
            }

            _radio.StartListening();
            Debug.Write(_radio.GetDetails());
        }

        public void WriteBytes([ReadOnlyArray] byte[] bytes)
        {
            //_rf.TransmitPayload(bytes);
            if (_role == Roles.RolePingOut)
            {
                // First, stop listening so we can talk.
                _radio.StopListening();

                // Take the time, and send it.  This will block until complete
                long time = DateTime.Now.Ticks;
                bool ok = _radio.Write(BitConverter.GetBytes(time));

                if (ok)
                    Debug.WriteLine("ok...");
                else
                    Debug.WriteLine("failed.");

                // Now, continue listening
                _radio.StartListening();

                // Wait here until we get a response, or timeout (250ms)
                Stopwatch stopwatch = Stopwatch.StartNew();
                bool timeout = false;
                while (!_radio.Available() && !timeout)
                    if (stopwatch.ElapsedMilliseconds > 200)
                        timeout = true;

                // Describe the results
                if (timeout)
                {
                    Debug.WriteLine("Failed, response timed out.");
                }
                else
                {
                    // Grab the response, compare, and send to debugging spew
                    byte[] gotTime = new byte[8];
                    _radio.Read(gotTime, 8);

                    // Spew it
                    Debug.WriteLine("Got response {0} round-trip delay:{1}", BitConverter.ToInt64(gotTime, 0),
                        stopwatch.ElapsedMilliseconds);
                }

                // Try again 1s later
                Task.Delay(1000).Wait();
            }
            //
            // Pong back role.  Receive each packet, dump it out, and send it back
            //

            if (_role == Roles.RolePongBack)
            {
                // if there is data ready
                if (_radio.Available())
                {
                    // Dump the payloads until we've gotten everything
                    byte[] gotTime = new byte[8];
                    bool done = false;
                    while (!done)
                    {
                        // Fetch the payload, and see if this was the last one.
                        done = _radio.Read(gotTime, 8);

                        // Spew it
                        Debug.WriteLine("Got payload {0}...", BitConverter.ToInt64(gotTime, 0));

                        // Delay just a little bit to let the other unit
                        // make the transition to receiver
                        Task.Delay(20).Wait();
                    }

                    // First, stop listening so we can talk
                    _radio.StopListening();

                    // Send the final one back.
                    _radio.Write(gotTime);
                    Debug.WriteLine("Sent response.");

                    // Now, resume listening so we catch the next packets.
                    _radio.StartListening();
                }
            }

            ////
            //// Change roles
            ////

            //char c = 'B';
            //if (c == 'T' && _role == Roles.RolePongBack)
            //{
            //    Debug.WriteLine("*** CHANGING TO TRANSMIT ROLE -- PRESS 'R' TO SWITCH BACK");

            //    // Become the primary transmitter (ping out)
            //    _role = Roles.RolePingOut;
            //    _radio.OpenWritingPipe(_pipes[0]);
            //    _radio.OpenReadingPipe(1, _pipes[1]);
            //}
            //else if (c == 'R' && _role == Roles.RolePingOut)
            //{
            //    Debug.WriteLine("*** CHANGING TO RECEIVE ROLE -- PRESS 'T' TO SWITCH BACK\n\r");

            //    // Become the primary receiver (pong back)
            //    _role = Roles.RolePongBack;
            //    _radio.OpenWritingPipe(_pipes[1]);
            //    _radio.OpenReadingPipe(1, _pipes[0]);
            //}
        }
    }
}
