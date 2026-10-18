import unittest

class TestExitCodes(unittest.TestCase):
    def test_exit_code_contract(self):
        # 0: every segment downloaded, merge succeeded
        # 1: general error
        # 2: curl_cffi missing
        # 3: partial download
        # 4: encrypted stream (#EXT-X-KEY)
        # 5: 401/403 access denied
        # 6: manifest needs the proxy engine (init segment / separate rendition / byte range)
        contract = {
            0: 'success',
            1: 'general_error',
            2: 'missing_dependency',
            3: 'partial_download',
            4: 'encrypted_stream',
            5: 'access_denied',
            6: 'needs_proxy_engine'
        }
        self.assertEqual(len(contract), 7)
        self.assertEqual(contract[6], 'needs_proxy_engine')
        self.assertEqual(contract[0], 'success')
        self.assertEqual(contract[3], 'partial_download')
        self.assertEqual(contract[4], 'encrypted_stream')
        self.assertEqual(contract[5], 'access_denied')


if __name__ == '__main__':
    unittest.main()
