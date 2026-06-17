-- Migration: Add matchId FK to CONVERSATION table for lobby chats
-- Run this against the Picklink database before deploying the new backend code.

ALTER TABLE [CONVERSATION]
ADD [matchId] INT NULL;

ALTER TABLE [CONVERSATION]
ADD CONSTRAINT [FK_CONVERSATION_MATCH]
FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]);

-- Optional index for lookup performance
CREATE INDEX [IX_CONVERSATION_matchId]
ON [CONVERSATION] ([matchId])
WHERE ([matchId] IS NOT NULL);
