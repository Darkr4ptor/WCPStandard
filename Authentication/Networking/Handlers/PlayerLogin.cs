﻿using System;
using System.Threading.Tasks;

using System.Linq;
using System.Collections;
using MySql.Data.MySqlClient;

using Core;
using Serilog;

namespace Authentication.Networking.Handlers
{
    class PlayerLogin : Networking.PacketHandler
    {
        protected override void Process(Entities.User user)
        {
            ArrayList userData = new ArrayList();

            string inputUserName = GetString(2);
            string inputPassword = GetString(3);

            bool isSettingNewNickName = false;

            //valid UserName?
            if (inputUserName.Length >= 3 && Core.Utils.isAlphaNumeric(inputUserName))
            {
                //is password long enough?
                if (inputPassword.Length >= 3)
                {
                    userData = DBQueryForUser(inputUserName);   
                    
                    //Does the username exists?
                    if (userData.Count > 0)
                    {
                        //The  user does exist:  retrieve data
                        uint id                  = Convert.ToUInt32(userData[0]);
                        string dbUserName        = inputUserName;
                        string displayname       = userData[2].ToString();
                        string dbPassword        = userData[3].ToString();
                        string dbPasswordSalt    = userData[4].ToString();

                        GameConstants.Rights dbRights;

                        try { dbRights = (GameConstants.Rights)Convert.ToByte(userData[5]); }
                        catch
                        { Log.Error("User " + dbUserName + " rights could not be parsed. Blocking user.");
                            dbRights = GameConstants.Rights.Blocked;
                        }
                       
                        //We hash password typed  by the player and check it against  the one stored in the DB
                        string hashedPassword = Core.Utils.CreateSHAHash(String.Concat(inputPassword, dbPasswordSalt));

                        //CHECK!! Proceed
                        if (hashedPassword == dbPassword.ToLower())
                        {
                            var IsOnline = Managers.SessionManager.Instance.Sessions.Select(n => n.Value).Where(n => n.ID == id && n.IsActivated && !n.IsEnded).Count();

                            //Check to see if the same account is already logged in
                            //TODO: Improve this. What if a GameServer does not update this?
                            if (IsOnline == 0)
                            {
                                //TODO: Add ban time? Delegate it to game servers?
                                //TODO: Add gameserver blacklisting
                                if (dbRights == GameConstants.Rights.Blocked)
                                    user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.Banned));
                                else
                                {
                                    //Authenticate player
                                    user.OnAuthorize(id, dbUserName, displayname);

                                    //check if the player has a NickName
                                    if (user.DisplayName.Length > 0)
                                        user.Send(new Packets.ServerList(user));

                                    else
                                    {
                                        if (Config.ENABLENICKCHANGE) //can they set their nickname ingame ???
                                        {
                                            isSettingNewNickName = true;
                                            user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.NewNickname));
                                        }
                                        else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.IlligalNickname)); }

                                    }
                                }
                               
                            }
                            else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.AlreadyLoggedIn)); }

                        }
                        else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.WrongPW)); }

                    }
                    else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.WrongUser)); }

                }
                else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.EnterPasswordError)); }

            }
            else { user.Send(new Packets.ServerList(Packets.ServerList.ErrorCodes.EnterIDError)); }


            //people who successfully logged on can be safely disconnected... 
            //the client will show the server list and they will be redirected by the client.
            //keep the socket for those who are setting up nickname
            if (!isSettingNewNickName)
                user.Disconnect();
        }

        //TODO: update with await and async methods
        private ArrayList DBQueryForUser(string inputUserName)
        {
            ArrayList dbData = new ArrayList();

            using (MySqlConnection connection = new MySqlConnection(Config.AUTH_CONNECTION))
            {
                try
                {
                    var commandQuery = connection.CreateCommand() as MySqlCommand;

                    commandQuery.CommandText = string.Concat("SELECT * FROM users WHERE username=", "'", inputUserName, "'", ";");
                    connection.Open();

                    MySqlDataReader Reader = commandQuery.ExecuteReader();  //asynch this?

                    if (Reader.HasRows && Reader.Read())
                    {
                        for(int i = 0; i < Reader.FieldCount; i++)
                        {
                            dbData.Add(Reader.GetValue(i));
                        }

                    }

                    Reader.Close();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }

            }

            return dbData;
        }

    }
}