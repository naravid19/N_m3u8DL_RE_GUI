import { test, beforeEach } from "node:test";
import assert from "node:assert/strict";
import { installFakeChrome } from "./helpers/fake-chrome.js";

const fake = installFakeChrome();
const { getSuiteVersion } = await import("../lib/suite-version.js");

beforeEach(() => fake.reset());

test("reads the generated suite version", async () => {
  fake.setResourceText("suite-version.json", "{ \"suiteVersion\": \"2.1.5\" }");

  assert.equal(await getSuiteVersion(), "2.1.5");
});

test("returns null when the file is missing rather than guessing", async () => {
  // Guessing low nags about the installed version; guessing high hides every
  // real update. Skipping is the only honest option.
  fake.setResourceMissing("suite-version.json");

  assert.equal(await getSuiteVersion(), null);
});

test("returns null on malformed JSON", async () => {
  fake.setResourceText("suite-version.json", "{ not json");

  assert.equal(await getSuiteVersion(), null);
});

test("returns null when the key is absent", async () => {
  fake.setResourceText("suite-version.json", "{ \"other\": \"2.1.5\" }");

  assert.equal(await getSuiteVersion(), null);
});

test("trims whitespace from the value", async () => {
  fake.setResourceText("suite-version.json", "{ \"suiteVersion\": \"  2.1.5  \" }");

  assert.equal(await getSuiteVersion(), "2.1.5");
});

